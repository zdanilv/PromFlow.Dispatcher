using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Infrastructure.Modbus.Archiving;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests.Archiving;

public sealed class ModbusArchiveCollectorTests
{
    [Fact]
    public async Task Collector_ClientSnapshot_EnqueuesHighResolutionRawSnapshot()
    {
        var runtime = new FakeModbusRuntimeService { CurrentOptions = CreateModbusOptions() };
        var ingestor = new CapturingArchiveIngestor();
        var collector = CreateCollector(runtime, ingestor, CreateArchiveOptions());
        await collector.StartAsync();
        var timestamp = new DateTimeOffset(2026, 6, 22, 12, 0, 0, TimeSpan.FromHours(3));

        runtime.PublishSnapshot(new ModbusSnapshot
        {
            Role = ModbusRuntimeRole.Client,
            Timestamp = timestamp,
            Coils = [true, false, true],
            HoldingRegisters = [1, 2]
        });

        var high = AssertSnapshotEnvelope(ingestor.Envelopes[0], ArchiveResolution.HighResolution);
        Assert.Equal(ArchivePriority.Telemetry, ingestor.Envelopes[0].Priority);
        Assert.Equal("device-1", high.DeviceId);
        Assert.Equal(ModbusRuntimeRole.Client, high.Role);
        Assert.Equal(1L, high.SequenceNumber);
        Assert.Equal(timestamp.ToUniversalTime(), high.CapturedAtUtc);
        Assert.Equal(10, high.CoilStartAddress);
        Assert.Equal(20, high.HoldingRegisterStartAddress);
        Assert.Equal([true, false, true], high.Coils);
        Assert.Equal([1, 2], high.HoldingRegisters);
        Assert.NotEmpty(high.ConfigurationHash);
    }

    [Fact]
    public async Task Collector_ServerSnapshot_UsesServerStartAddresses()
    {
        var runtime = new FakeModbusRuntimeService { CurrentOptions = CreateModbusOptions() };
        var ingestor = new CapturingArchiveIngestor();
        var collector = CreateCollector(runtime, ingestor, CreateArchiveOptions());
        await collector.StartAsync();

        runtime.PublishSnapshot(new ModbusSnapshot
        {
            Role = ModbusRuntimeRole.Server,
            Timestamp = DateTimeOffset.UtcNow,
            Coils = [false],
            HoldingRegisters = [9]
        });

        var high = AssertSnapshotEnvelope(ingestor.Envelopes[0], ArchiveResolution.HighResolution);
        Assert.Equal(ModbusRuntimeRole.Server, high.Role);
        Assert.Equal(100, high.CoilStartAddress);
        Assert.Equal(200, high.HoldingRegisterStartAddress);
    }

    [Fact]
    public async Task Collector_UnsupportedRuntimeRole_IgnoresSnapshot()
    {
        var runtime = new FakeModbusRuntimeService { CurrentOptions = CreateModbusOptions() };
        var ingestor = new CapturingArchiveIngestor();
        var collector = CreateCollector(runtime, ingestor, CreateArchiveOptions());
        await collector.StartAsync();

        runtime.PublishSnapshot(new ModbusSnapshot
        {
            Role = ModbusRuntimeRole.Runtime,
            Timestamp = DateTimeOffset.UtcNow,
            Coils = [true],
            HoldingRegisters = [1]
        });

        Assert.Empty(ingestor.Envelopes);
    }

    [Fact]
    public async Task Collector_DisabledArchive_DoesNotEnqueue()
    {
        var runtime = new FakeModbusRuntimeService { CurrentOptions = CreateModbusOptions() };
        var ingestor = new CapturingArchiveIngestor();
        var options = CreateArchiveOptions();
        options.Enabled = false;
        var collector = CreateCollector(runtime, ingestor, options);
        await collector.StartAsync();

        runtime.PublishSnapshot(new ModbusSnapshot
        {
            Role = ModbusRuntimeRole.Client,
            Timestamp = DateTimeOffset.UtcNow,
            Coils = [true],
            HoldingRegisters = [1]
        });
        runtime.PublishStatus(new ModbusStatus { ClientState = ModbusConnectionState.Running });

        Assert.Empty(ingestor.Envelopes);
    }

    [Fact]
    public async Task Collector_SnapshotDefensivelyCopiesCoilsAndRegisters()
    {
        var runtime = new FakeModbusRuntimeService { CurrentOptions = CreateModbusOptions() };
        var ingestor = new CapturingArchiveIngestor();
        var collector = CreateCollector(runtime, ingestor, CreateArchiveOptions());
        await collector.StartAsync();
        var coils = new[] { true, false };
        var registers = new ushort[] { 1, 2 };

        runtime.PublishSnapshot(new ModbusSnapshot
        {
            Role = ModbusRuntimeRole.Client,
            Timestamp = DateTimeOffset.UtcNow,
            Coils = coils,
            HoldingRegisters = registers
        });
        coils[0] = false;
        registers[0] = 99;

        var high = AssertSnapshotEnvelope(ingestor.Envelopes[0], ArchiveResolution.HighResolution);
        Assert.Equal([true, false], high.Coils);
        Assert.Equal([1, 2], high.HoldingRegisters);
    }

    [Fact]
    public async Task Collector_SequencesAreMonotonic()
    {
        var runtime = new FakeModbusRuntimeService { CurrentOptions = CreateModbusOptions() };
        var ingestor = new CapturingArchiveIngestor();
        var collector = CreateCollector(runtime, ingestor, CreateArchiveOptions());
        await collector.StartAsync();

        runtime.PublishSnapshot(CreateSnapshot(ModbusRuntimeRole.Client, DateTimeOffset.UtcNow));
        runtime.PublishSnapshot(CreateSnapshot(ModbusRuntimeRole.Client, DateTimeOffset.UtcNow.AddMilliseconds(1)));

        var first = AssertSnapshotEnvelope(ingestor.Envelopes[0], ArchiveResolution.HighResolution);
        var second = AssertSnapshotEnvelope(ingestor.Envelopes[2], ArchiveResolution.HighResolution);
        Assert.Equal(1L, first.SequenceNumber);
        Assert.Equal(2L, second.SequenceNumber);
    }

    [Fact]
    public async Task Collector_LongTermSampling_EmitsOnlyWhenIntervalElapsed()
    {
        var runtime = new FakeModbusRuntimeService { CurrentOptions = CreateModbusOptions() };
        var ingestor = new CapturingArchiveIngestor();
        var archiveOptions = CreateArchiveOptions();
        archiveOptions.LongTermSnapshotIntervalMs = 1000;
        var collector = CreateCollector(runtime, ingestor, archiveOptions);
        await collector.StartAsync();
        var start = new DateTimeOffset(2026, 6, 22, 10, 0, 0, TimeSpan.Zero);

        runtime.PublishSnapshot(CreateSnapshot(ModbusRuntimeRole.Client, start));
        runtime.PublishSnapshot(CreateSnapshot(ModbusRuntimeRole.Client, start.AddMilliseconds(999)));
        runtime.PublishSnapshot(CreateSnapshot(ModbusRuntimeRole.Client, start.AddMilliseconds(1000)));

        Assert.Equal(
            [
                ArchiveResolution.HighResolution,
                ArchiveResolution.LongTerm,
                ArchiveResolution.HighResolution,
                ArchiveResolution.HighResolution,
                ArchiveResolution.LongTerm
            ],
            ingestor.Envelopes
                .Select(envelope => ((RawModbusSnapshotArchiveRecord)envelope.Record).Resolution)
                .ToArray());
        Assert.Equal(
            [1L, 1L, 2L, 3L, 3L],
            ingestor.Envelopes
                .Select(envelope => ((RawModbusSnapshotArchiveRecord)envelope.Record).SequenceNumber)
                .ToArray());
    }

    [Fact]
    public async Task Collector_StatusDuplicate_IsSuppressed()
    {
        var runtime = new FakeModbusRuntimeService { CurrentOptions = CreateModbusOptions() };
        var ingestor = new CapturingArchiveIngestor();
        var collector = CreateCollector(runtime, ingestor, CreateArchiveOptions());
        await collector.StartAsync();
        var status = new ModbusStatus
        {
            ClientState = ModbusConnectionState.Running,
            ServerState = ModbusConnectionState.Stopped,
            ClientMessage = "client",
            ServerMessage = "server",
            UpdatedAt = DateTimeOffset.UtcNow
        };

        runtime.PublishStatus(status);
        runtime.PublishStatus(status);

        var envelope = Assert.Single(ingestor.Envelopes);
        Assert.Equal(ArchiveRecordKind.ModbusStatus, envelope.Kind);
        Assert.Equal(ArchivePriority.Normal, envelope.Priority);
    }

    [Fact]
    public async Task Collector_StatusTransition_EnqueuesModbusStatusRecord()
    {
        var runtime = new FakeModbusRuntimeService { CurrentOptions = CreateModbusOptions() };
        var ingestor = new CapturingArchiveIngestor();
        var collector = CreateCollector(runtime, ingestor, CreateArchiveOptions());
        await collector.StartAsync();

        runtime.PublishStatus(new ModbusStatus
        {
            ClientState = ModbusConnectionState.Running,
            ServerState = ModbusConnectionState.Stopped,
            ClientMessage = "running",
            ServerMessage = "stopped",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        runtime.PublishStatus(new ModbusStatus
        {
            ClientState = ModbusConnectionState.Faulted,
            ServerState = ModbusConnectionState.Stopped,
            ClientMessage = "faulted",
            ServerMessage = "stopped",
            LastError = "boom",
            UpdatedAt = DateTimeOffset.UtcNow.AddSeconds(1)
        });

        Assert.Equal(2, ingestor.Envelopes.Count);
        var record = Assert.IsType<ModbusStatusArchiveRecord>(ingestor.Envelopes[1].Record);
        Assert.Equal("device-1", record.DeviceId);
        Assert.Equal(ModbusConnectionState.Faulted, record.ClientState);
        Assert.Equal("boom", record.LastError);
    }

    [Fact]
    public async Task Collector_Stop_UnsubscribesFromRuntimeEvents()
    {
        var runtime = new FakeModbusRuntimeService { CurrentOptions = CreateModbusOptions() };
        var ingestor = new CapturingArchiveIngestor();
        var collector = CreateCollector(runtime, ingestor, CreateArchiveOptions());
        await collector.StartAsync();
        await collector.StopAsync();

        runtime.PublishSnapshot(CreateSnapshot(ModbusRuntimeRole.Client, DateTimeOffset.UtcNow));
        runtime.PublishStatus(new ModbusStatus { ClientState = ModbusConnectionState.Running });

        Assert.Empty(ingestor.Envelopes);
    }

    [Fact]
    public async Task Collector_StartStop_AreIdempotent()
    {
        var runtime = new FakeModbusRuntimeService { CurrentOptions = CreateModbusOptions() };
        var ingestor = new CapturingArchiveIngestor();
        var collector = CreateCollector(runtime, ingestor, CreateArchiveOptions());

        await collector.StartAsync();
        await collector.StartAsync();
        runtime.PublishSnapshot(CreateSnapshot(ModbusRuntimeRole.Client, DateTimeOffset.UtcNow));
        await collector.StopAsync();
        await collector.StopAsync();
        runtime.PublishSnapshot(CreateSnapshot(ModbusRuntimeRole.Client, DateTimeOffset.UtcNow.AddSeconds(1)));

        Assert.Equal(2, ingestor.Envelopes.Count);
    }

    [Fact]
    public async Task Collector_IncompleteEnqueue_DoesNotBlockCallback()
    {
        var runtime = new FakeModbusRuntimeService { CurrentOptions = CreateModbusOptions() };
        var pending = new TaskCompletionSource<ArchiveOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ingestor = new CapturingArchiveIngestor
        {
            OnEnqueue = envelope => new ValueTask<ArchiveOperationResult>(pending.Task)
        };
        var collector = CreateCollector(runtime, ingestor, CreateArchiveOptions());
        await collector.StartAsync();

        runtime.PublishSnapshot(CreateSnapshot(ModbusRuntimeRole.Client, DateTimeOffset.UtcNow));

        Assert.False(pending.Task.IsCompleted);
        pending.SetResult(ArchiveOperationResult.Success());
    }

    [Fact]
    public async Task Collector_EnqueueFailure_DoesNotThrow()
    {
        var runtime = new FakeModbusRuntimeService { CurrentOptions = CreateModbusOptions() };
        var ingestor = new CapturingArchiveIngestor
        {
            OnEnqueue = envelope => ValueTask.FromResult(ArchiveOperationResult.Failure("Failure", "Nope"))
        };
        var collector = CreateCollector(runtime, ingestor, CreateArchiveOptions());
        await collector.StartAsync();

        runtime.PublishStatus(new ModbusStatus { ClientState = ModbusConnectionState.Running });

        Assert.True(true);
    }

    [Fact]
    public async Task ModbusDependencyInjection_RegistersArchiveCollectorWhenArchiveIngestorIsAvailable()
    {
        var services = new ServiceCollection();
        AddNullLogging(services);
        services.AddOptions();
        services.AddSingleton<IModbusClientService, StubModbusClientService>();
        services.AddSingleton<IModbusServerService, StubModbusServerService>();
        services.AddSingleton<IArchiveIngestor, CapturingArchiveIngestor>();
        services.AddModbusInfrastructure(new ConfigurationBuilder().Build());
        services.Configure<ArchiveOptions>(options =>
        {
            options.Enabled = true;
            options.DeviceId = "device-1";
        });

        await using var serviceProvider = services.BuildServiceProvider();

        Assert.IsType<ModbusArchiveCollector>(serviceProvider.GetRequiredService<IModbusArchiveCollector>());
    }

    private static ModbusArchiveCollector CreateCollector(
        IModbusRuntimeService runtime,
        IArchiveIngestor ingestor,
        ArchiveOptions options)
        => new(
            runtime,
            ingestor,
            new FixedOptionsMonitor<ArchiveOptions>(options),
            new ModbusConfigurationFingerprint(),
            NullLogger<ModbusArchiveCollector>.Instance);

    private static RawModbusSnapshotArchiveRecord AssertSnapshotEnvelope(
        ArchiveEnvelope envelope,
        ArchiveResolution resolution)
    {
        Assert.Equal(ArchiveRecordKind.RawModbusSnapshot, envelope.Kind);
        var record = Assert.IsType<RawModbusSnapshotArchiveRecord>(envelope.Record);
        Assert.Equal(resolution, record.Resolution);
        return record;
    }

    private static ModbusSnapshot CreateSnapshot(ModbusRuntimeRole role, DateTimeOffset timestamp)
        => new()
        {
            Role = role,
            Timestamp = timestamp,
            Coils = [true],
            HoldingRegisters = [1]
        };

    private static ArchiveOptions CreateArchiveOptions()
        => new()
        {
            Enabled = true,
            DeviceId = "device-1",
            LongTermSnapshotIntervalMs = 1000
        };

    private static ModbusOptions CreateModbusOptions()
        => new()
        {
            Client = new ModbusEndpointOptions
            {
                CoilStartAddress = 10,
                HoldingRegisterStartAddress = 20,
                CoilCount = 4,
                RegisterCount = 4
            },
            Server = new ModbusEndpointOptions
            {
                CoilStartAddress = 100,
                HoldingRegisterStartAddress = 200,
                CoilCount = 8,
                RegisterCount = 8
            },
            DataMap =
            [
                new ModbusDataPointOptions
                {
                    Name = "Signal",
                    Area = ModbusDataArea.HoldingRegister,
                    Address = 1,
                    Length = 1,
                    Type = ModbusValueType.UInt16
                }
            ]
        };

    private static void AddNullLogging(IServiceCollection services)
    {
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    }

    private sealed class CapturingArchiveIngestor : IArchiveIngestor
    {
        public List<ArchiveEnvelope> Envelopes { get; } = [];

        public Func<ArchiveEnvelope, ValueTask<ArchiveOperationResult>>? OnEnqueue { get; init; }

        public ValueTask<ArchiveOperationResult> EnqueueAsync(
            ArchiveEnvelope envelope,
            CancellationToken cancellationToken)
        {
            Envelopes.Add(envelope);
            if (OnEnqueue is not null)
            {
                return OnEnqueue(envelope);
            }

            return ValueTask.FromResult(ArchiveOperationResult.Success());
        }
    }

    private sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;

        public T Get(string? name)
            => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener)
            => null;
    }

    private sealed class FakeModbusRuntimeService : IModbusRuntimeService
    {
        public ModbusStatus Status { get; private set; } = ModbusStatus.Stopped;

        public ModbusSnapshot ClientSnapshot { get; private set; } = ModbusSnapshot.Empty;

        public ModbusSnapshot ServerSnapshot { get; private set; } = ModbusSnapshot.Empty;

        public ModbusOptions CurrentOptions { get; init; } = new();

        public event EventHandler<ModbusStatus>? StatusChanged;

        public event EventHandler<ModbusSnapshot>? SnapshotChanged;

        public void PublishStatus(ModbusStatus status)
        {
            Status = status;
            StatusChanged?.Invoke(this, status);
        }

        public void PublishSnapshot(ModbusSnapshot snapshot)
        {
            if (snapshot.Role == ModbusRuntimeRole.Client)
            {
                ClientSnapshot = snapshot;
            }
            else if (snapshot.Role == ModbusRuntimeRole.Server)
            {
                ServerSnapshot = snapshot;
            }

            SnapshotChanged?.Invoke(this, snapshot);
        }

        public Task StartAsync(
            ModbusRunMode mode = ModbusRunMode.Both,
            ModbusOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RestartAsync(
            ModbusRunMode mode = ModbusRunMode.Both,
            ModbusOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopClientAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RestartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopServerAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RestartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

    private sealed class StubModbusClientService : IModbusClientService
    {
        public ModbusConnectionState State { get; } = ModbusConnectionState.Stopped;

        public ModbusSnapshot Snapshot { get; } = ModbusSnapshot.Empty;

        public event EventHandler<ModbusStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<ModbusSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task StartAsync(ModbusEndpointOptions options, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task WriteCoilAsync(int address, bool value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task WriteRegisterAsync(int address, ushort value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task WriteRegistersAsync(int startAddress, ushort[] values, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

    private sealed class StubModbusServerService : IModbusServerService
    {
        public ModbusConnectionState State { get; } = ModbusConnectionState.Stopped;

        public ModbusSnapshot Snapshot { get; } = ModbusSnapshot.Empty;

        public event EventHandler<ModbusStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<ModbusSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task StartAsync(ModbusEndpointOptions options, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetCoilAsync(int address, bool value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetRegisterAsync(int address, ushort value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetRegistersAsync(int startAddress, ushort[] values, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }
}
