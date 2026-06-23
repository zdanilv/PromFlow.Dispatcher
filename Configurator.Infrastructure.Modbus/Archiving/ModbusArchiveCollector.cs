using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Configurator.Infrastructure.Modbus.Archiving;

public sealed class ModbusArchiveCollector : IModbusArchiveCollector
{
    private readonly object _gate = new();
    private readonly IModbusRuntimeService _runtime;
    private readonly IArchiveIngestor _ingestor;
    private readonly IOptionsMonitor<ArchiveOptions> _archiveOptions;
    private readonly ModbusConfigurationFingerprint _fingerprint;
    private readonly ILogger<ModbusArchiveCollector> _logger;
    private bool _started;
    private bool _disposed;
    private long _sequence;
    private DateTimeOffset? _lastClientLongTermUtc;
    private DateTimeOffset? _lastServerLongTermUtc;
    private StatusKey? _lastStatus;

    public ModbusArchiveCollector(
        IModbusRuntimeService runtime,
        IArchiveIngestor ingestor,
        IOptionsMonitor<ArchiveOptions> archiveOptions,
        ModbusConfigurationFingerprint fingerprint,
        ILogger<ModbusArchiveCollector> logger)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _ingestor = ingestor ?? throw new ArgumentNullException(nameof(ingestor));
        _archiveOptions = archiveOptions ?? throw new ArgumentNullException(nameof(archiveOptions));
        _fingerprint = fingerprint ?? throw new ArgumentNullException(nameof(fingerprint));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<ArchiveOperationResult> StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_disposed)
            {
                return Task.FromResult(ArchiveOperationResult.Failure(
                    "ModbusArchiveCollectorDisposed",
                    "Modbus archive collector has been disposed."));
            }

            if (_started)
            {
                return Task.FromResult(ArchiveOperationResult.Success());
            }

            _runtime.SnapshotChanged += OnSnapshotChanged;
            _runtime.StatusChanged += OnStatusChanged;
            _started = true;
        }

        return Task.FromResult(ArchiveOperationResult.Success());
    }

    public Task<ArchiveOperationResult> StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_started)
            {
                return Task.FromResult(ArchiveOperationResult.Success());
            }

            _runtime.SnapshotChanged -= OnSnapshotChanged;
            _runtime.StatusChanged -= OnStatusChanged;
            _started = false;
        }

        return Task.FromResult(ArchiveOperationResult.Success());
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);

        lock (_gate)
        {
            _disposed = true;
        }
    }

    private void OnSnapshotChanged(object? sender, ModbusSnapshot snapshot)
    {
        try
        {
            if (!TryGetArchiveOptions(out var options))
            {
                return;
            }

            if (snapshot.Role is not (ModbusRuntimeRole.Client or ModbusRuntimeRole.Server))
            {
                return;
            }

            var runtimeOptions = _runtime.CurrentOptions.Clone();
            var endpoint = GetEndpoint(runtimeOptions, snapshot.Role);
            var capturedAtUtc = snapshot.Timestamp.ToUniversalTime();
            var coils = snapshot.Coils.ToArray();
            var holdingRegisters = snapshot.HoldingRegisters.ToArray();
            var sequence = Interlocked.Increment(ref _sequence);
            var configurationHash = _fingerprint.Compute(runtimeOptions, snapshot.Role);

            EnqueueSnapshot(
                options,
                snapshot.Role,
                sequence,
                capturedAtUtc,
                endpoint,
                coils,
                holdingRegisters,
                configurationHash,
                ArchiveResolution.HighResolution);

            if (ShouldEmitLongTerm(snapshot.Role, capturedAtUtc, options.LongTermSnapshotIntervalMs))
            {
                EnqueueSnapshot(
                    options,
                    snapshot.Role,
                    sequence,
                    capturedAtUtc,
                    endpoint,
                    coils,
                    holdingRegisters,
                    configurationHash,
                    ArchiveResolution.LongTerm);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Modbus snapshot archive collection skipped.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected Modbus snapshot archive collection failure.");
        }
    }

    private void OnStatusChanged(object? sender, ModbusStatus status)
    {
        try
        {
            if (!TryGetArchiveOptions(out var options))
            {
                return;
            }

            var key = new StatusKey(
                status.ClientState,
                status.ServerState,
                status.ClientMessage,
                status.ServerMessage,
                status.LastError);

            lock (_gate)
            {
                if (Equals(_lastStatus, key))
                {
                    return;
                }

                _lastStatus = key;
            }

            var recordId = Guid.NewGuid();
            var record = new ModbusStatusArchiveRecord(
                recordId,
                options.DeviceId,
                status.UpdatedAt.ToUniversalTime(),
                status.ClientState,
                status.ServerState,
                status.ClientMessage,
                status.ServerMessage,
                status.LastError,
                schemaVersion: 1);
            var envelope = new ArchiveEnvelope(
                recordId,
                ArchiveRecordKind.ModbusStatus,
                ArchivePriority.Normal,
                record,
                DateTimeOffset.UtcNow);

            EnqueueWithoutBlocking(envelope);
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Modbus status archive collection skipped.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected Modbus status archive collection failure.");
        }
    }

    private bool TryGetArchiveOptions(out ArchiveOptions options)
    {
        options = _archiveOptions.CurrentValue.Clone();
        if (!options.Enabled)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.DeviceId))
        {
            _logger.LogWarning("Archive is enabled, but Archive DeviceId is empty. Modbus archive record skipped.");
            return false;
        }

        options.DeviceId = options.DeviceId.Trim();
        return true;
    }

    private void EnqueueSnapshot(
        ArchiveOptions options,
        ModbusRuntimeRole role,
        long sequence,
        DateTimeOffset capturedAtUtc,
        ModbusEndpointOptions endpoint,
        IReadOnlyList<bool> coils,
        IReadOnlyList<ushort> holdingRegisters,
        string configurationHash,
        ArchiveResolution resolution)
    {
        var recordId = Guid.NewGuid();
        var record = new RawModbusSnapshotArchiveRecord(
            recordId,
            options.DeviceId,
            role,
            sequence,
            capturedAtUtc,
            endpoint.CoilStartAddress,
            endpoint.HoldingRegisterStartAddress,
            coils,
            holdingRegisters,
            configurationHash,
            resolution,
            schemaVersion: 1);
        var envelope = new ArchiveEnvelope(
            recordId,
            ArchiveRecordKind.RawModbusSnapshot,
            ArchivePriority.Telemetry,
            record,
            DateTimeOffset.UtcNow);

        EnqueueWithoutBlocking(envelope);
    }

    private void EnqueueWithoutBlocking(ArchiveEnvelope envelope)
    {
        try
        {
            var enqueueTask = _ingestor.EnqueueAsync(envelope, CancellationToken.None).AsTask();
            _ = ObserveEnqueueAsync(enqueueTask, envelope.Kind);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Archive enqueue failed before completion for {ArchiveRecordKind}.", envelope.Kind);
        }
    }

    private async Task ObserveEnqueueAsync(Task<ArchiveOperationResult> enqueueTask, ArchiveRecordKind kind)
    {
        try
        {
            var result = await enqueueTask.ConfigureAwait(false);
            if (!result.Succeeded)
            {
                _logger.LogWarning(
                    "Archive enqueue failed for {ArchiveRecordKind}: {ErrorCode} {ErrorMessage}",
                    kind,
                    result.ErrorCode,
                    result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Archive enqueue observation failed for {ArchiveRecordKind}.", kind);
        }
    }

    private bool ShouldEmitLongTerm(
        ModbusRuntimeRole role,
        DateTimeOffset capturedAtUtc,
        int intervalMs)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Max(1, intervalMs));

        lock (_gate)
        {
            if (role == ModbusRuntimeRole.Client)
            {
                if (_lastClientLongTermUtc is null || capturedAtUtc - _lastClientLongTermUtc.Value >= interval)
                {
                    _lastClientLongTermUtc = capturedAtUtc;
                    return true;
                }

                return false;
            }

            if (_lastServerLongTermUtc is null || capturedAtUtc - _lastServerLongTermUtc.Value >= interval)
            {
                _lastServerLongTermUtc = capturedAtUtc;
                return true;
            }

            return false;
        }
    }

    private static ModbusEndpointOptions GetEndpoint(ModbusOptions options, ModbusRuntimeRole role)
        => role switch
        {
            ModbusRuntimeRole.Client => options.Client,
            ModbusRuntimeRole.Server => options.Server,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Only client and server roles can be archived.")
        };

    private sealed record StatusKey(
        ModbusConnectionState ClientState,
        ModbusConnectionState ServerState,
        string ClientMessage,
        string ServerMessage,
        string? LastError);
}
