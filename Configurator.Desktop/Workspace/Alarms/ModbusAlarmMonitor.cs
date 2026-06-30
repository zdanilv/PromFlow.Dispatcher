using Avalonia.Threading;
using Configurator.Application.Services.Dialogs;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Configurator.Desktop.Workspace.Alarms;

public sealed class ModbusAlarmMonitor : IDisposable
{
    private readonly IOptionsMonitor<ModbusOptions> _optionsMonitor;
    private readonly IModbusRuntimeService _runtime;
    private readonly IDialogService _dialogService;
    private readonly IModbusBitWriter _bitWriter;
    private readonly RouteMapSessionJournal _sessionJournal;
    private readonly ILogger<ModbusAlarmMonitor> _logger;
    private readonly Dictionary<string, AlarmRuntimeState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private IDisposable? _optionsSubscription;
    private ModbusOptions _options;
    private bool _started;
    private bool _disposed;

    public ModbusAlarmMonitor(
        IOptionsMonitor<ModbusOptions> optionsMonitor,
        IModbusRuntimeService runtime,
        IDialogService dialogService,
        IModbusBitWriter bitWriter,
        RouteMapSessionJournal sessionJournal,
        ILogger<ModbusAlarmMonitor> logger)
    {
        _optionsMonitor = optionsMonitor;
        _runtime = runtime;
        _dialogService = dialogService;
        _bitWriter = bitWriter;
        _sessionJournal = sessionJournal;
        _logger = logger;
        _options = optionsMonitor.CurrentValue.Clone();
    }

    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        _runtime.SnapshotChanged += OnSnapshotChanged;
        _optionsSubscription = _optionsMonitor.OnChange((options, name) =>
        {
            if (string.Equals(name, ModbusOptions.DemoSectionName, StringComparison.Ordinal))
            {
                return;
            }

            lock (_sync)
            {
                _options = options.Clone();
                PruneStates(_options.AlarmMap);
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runtime.SnapshotChanged -= OnSnapshotChanged;
        _optionsSubscription?.Dispose();
    }

    internal async Task ProcessSnapshotAsync(
        ModbusSnapshot snapshot,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ModbusAlarmOptions[] alarms;
        lock (_sync)
        {
            alarms = _options.AlarmMap
                .Where(alarm => alarm.Enabled)
                .Select(alarm => alarm.Clone())
                .ToArray();
            PruneStates(alarms);
        }

        foreach (var alarm in alarms)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var isActive = TryReadBit(snapshot, alarm.Alarm, out var value) && value;
            AlarmRuntimeState state;
            lock (_sync)
            {
                state = GetState(alarm.Id);
                if (!isActive)
                {
                    if (state.IsActive)
                    {
                        _sessionJournal.RecordAlarmCleared(alarm, now);
                    }

                    state.IsActive = false;
                    state.ActivatedAt = null;
                    state.ShouldMarkNotificationUnread = false;
                    state.NextShowAt = null;
                    state.IsShowing = false;
                    continue;
                }

                if (!state.IsActive)
                {
                    state.IsActive = true;
                    state.ActivatedAt = now;
                    state.ShouldMarkNotificationUnread = true;
                    state.NextShowAt = now;
                    _sessionJournal.RecordAlarmActivated(alarm, now);
                }

                if (state.IsShowing || state.NextShowAt is { } nextShowAt && now < nextShowAt)
                {
                    continue;
                }

                state.IsShowing = true;
            }

            try
            {
                DateTimeOffset createdAt;
                bool markUnread;
                lock (_sync)
                {
                    state = GetState(alarm.Id);
                    createdAt = state.ActivatedAt ?? now;
                    markUnread = state.ShouldMarkNotificationUnread;
                    state.ShouldMarkNotificationUnread = false;
                }

                _sessionJournal.ShowAlarmNotification(alarm, createdAt, markUnread);
                var confirmed = await _dialogService.ShowAlarmNotificationAsync(
                    alarm.Kind,
                    alarm.Message,
                    cancellationToken);
                if (confirmed)
                {
                    _sessionJournal.RecordAlarmAcknowledged(alarm, DateTimeOffset.Now);
                    if (_sessionJournal.IsAlarmActive(alarm.Id))
                    {
                        var acknowledgement = await _bitWriter.PulseAsync(
                            alarm.Acknowledgement,
                            alarm.AcknowledgementPulseDurationMs,
                            cancellationToken);
                        if (!acknowledgement.Succeeded)
                        {
                            _logger.LogWarning(
                                "Failed to write acknowledgement for alarm {AlarmId}: {Error}",
                                alarm.Id,
                                acknowledgement.ErrorMessage);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Alarm notification failed for {AlarmId}", alarm.Id);
            }
            finally
            {
                lock (_sync)
                {
                    var updated = GetState(alarm.Id);
                    updated.IsShowing = false;
                    updated.NextShowAt = now.AddMilliseconds(alarm.RepeatIntervalMs);
                }
            }
        }
    }

    private void OnSnapshotChanged(object? sender, ModbusSnapshot snapshot)
    {
        if (_disposed || !ShouldProcessSnapshot(snapshot))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            _ = ProcessSnapshotAsync(snapshot, DateTimeOffset.UtcNow);
        });
    }

    private bool ShouldProcessSnapshot(ModbusSnapshot snapshot)
    {
        var status = _runtime.Status;
        if (status.ClientState == ModbusConnectionState.Running)
        {
            return snapshot.Role == ModbusRuntimeRole.Client;
        }

        if (status.ServerState == ModbusConnectionState.Running)
        {
            return snapshot.Role == ModbusRuntimeRole.Server;
        }

        return false;
    }

    private AlarmRuntimeState GetState(string alarmId)
    {
        if (!_states.TryGetValue(alarmId, out var state))
        {
            state = new AlarmRuntimeState();
            _states[alarmId] = state;
        }

        return state;
    }

    private void PruneStates(IReadOnlyList<ModbusAlarmOptions> alarms)
    {
        var knownIds = alarms
            .Select(alarm => alarm.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stateId in _states.Keys.Where(id => !knownIds.Contains(id)).ToArray())
        {
            _states.Remove(stateId);
        }
    }

    private static bool TryReadBit(
        ModbusSnapshot snapshot,
        ModbusBitAddressOptions address,
        out bool value)
    {
        if (address.Area == ModbusDataArea.Coil)
        {
            if (address.Address >= 0 && address.Address < snapshot.Coils.Count)
            {
                value = snapshot.Coils[address.Address];
                return true;
            }

            value = false;
            return false;
        }

        if (address.Area == ModbusDataArea.HoldingRegister
            && address.BitIndex is >= 0 and <= 15
            && address.Address >= 0
            && address.Address < snapshot.HoldingRegisters.Count)
        {
            value = (snapshot.HoldingRegisters[address.Address] & (1 << address.BitIndex.Value)) != 0;
            return true;
        }

        value = false;
        return false;
    }

    private sealed class AlarmRuntimeState
    {
        public bool IsActive { get; set; }
        public bool IsShowing { get; set; }
        public DateTimeOffset? NextShowAt { get; set; }
        public DateTimeOffset? ActivatedAt { get; set; }
        public bool ShouldMarkNotificationUnread { get; set; }
    }
}
