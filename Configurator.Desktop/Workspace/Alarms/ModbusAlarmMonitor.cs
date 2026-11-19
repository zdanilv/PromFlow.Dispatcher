using System.Globalization;
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
    private long _configurationGeneration;
    private long _activationToken;
    private (ModbusConnectionState Client, ModbusConnectionState Server) _runtimeStates;

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
        _runtimeStates = (runtime.Status.ClientState, runtime.Status.ServerState);
    }

    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        _runtime.SnapshotChanged += OnSnapshotChanged;
        _runtime.StatusChanged += OnStatusChanged;
        _optionsSubscription = _optionsMonitor.OnChange((options, name) =>
        {
            AlarmRuntimeState[] invalidated;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                // ModbusDemo owns the TCP runtime, but never replaces Modbus.AlarmMap.
                if (!string.Equals(name, ModbusOptions.DemoSectionName, StringComparison.Ordinal))
                {
                    if (SameAlarmMap(_options.AlarmMap, options.AlarmMap))
                    {
                        return;
                    }

                    _options = options.Clone();
                    invalidated = InvalidateStates(state => !_options.AlarmMap.Any(alarm =>
                        alarm.Enabled && SameAlarm(alarm, state.Context.CopyAlarm())));
                }
                else
                {
                    invalidated = InvalidateStates();
                }
            }

            CancelStates(invalidated);
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        AlarmRuntimeState[] invalidated;
        lock (_sync)
        {
            _disposed = true;
            invalidated = InvalidateStates();
        }

        _runtime.SnapshotChanged -= OnSnapshotChanged;
        _runtime.StatusChanged -= OnStatusChanged;
        _optionsSubscription?.Dispose();
        CancelStates(invalidated);
    }

    internal async Task ProcessSnapshotAsync(
        ModbusSnapshot snapshot,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ModbusAlarmOptions[] alarms;
        long generation;
        AlarmRuntimeState[] invalidated;
        lock (_sync)
        {
            if (_disposed || snapshot.Role != GetWriteRole(_runtime.Status))
            {
                return;
            }

            // A start of an already running role can replace CurrentOptions without
            // a status transition. Recover on the next snapshot instead of retaining
            // an invalid state whose IsShowing would otherwise remain set forever.
            var runtimeOptions = _runtime.CurrentOptions;
            invalidated = _states.Values.Any(state => !ReferenceEquals(state.RuntimeOptions, runtimeOptions)
                                                      || state.Role != snapshot.Role)
                ? InvalidateStates(state => !ReferenceEquals(state.RuntimeOptions, runtimeOptions)
                                            || state.Role != snapshot.Role)
                : [];
            generation = _configurationGeneration;
            alarms = _options.AlarmMap
                .Where(alarm => alarm.Enabled)
                .Select(alarm => alarm.Clone())
                .ToArray();
        }

        CancelStates(invalidated);

        foreach (var alarm in alarms)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var isActive = TryReadBit(snapshot, alarm.Alarm, out var value) && value;
            AlarmRuntimeState state;
            AlarmRuntimeState? cleared = null;
            var activated = false;
            var show = false;
            var markUnread = false;
            lock (_sync)
            {
                if (_disposed || generation != _configurationGeneration)
                {
                    return;
                }

                _states.TryGetValue(alarm.Id, out var existing);
                if (!isActive)
                {
                    if (existing is not null)
                    {
                        _states.Remove(alarm.Id);
                        cleared = existing;
                    }
                }
                else if (existing is null)
                {
                    existing = new AlarmRuntimeState(now, snapshot.Role, _runtime.CurrentOptions);
                    var captured = existing;
                    existing.Context = new AlarmAcknowledgementContext(
                        alarm, ++_activationToken, generation,
                        () => IsCurrent(alarm.Id, captured, generation), existing.Lifetime.Token);
                    _states[alarm.Id] = existing;
                    activated = true;
                }

                state = existing!;
                if (isActive && !state.IsShowing
                    && (state.NextShowAt is not { } nextShowAt || now >= nextShowAt))
                {
                    state.IsShowing = true;
                    markUnread = state.ShouldMarkNotificationUnread;
                    state.ShouldMarkNotificationUnread = false;
                    show = true;
                }
            }

            if (cleared is not null)
            {
                cleared.Lifetime.Cancel();
                _sessionJournal.RecordAlarmCleared(alarm, now);
            }

            if (activated)
            {
                _sessionJournal.RecordAlarmActivated(alarm, now);
            }

            if (!show || !state.Context.IsCurrent())
            {
                continue;
            }

            try
            {
                var notification = CreateNotificationContent(alarm, snapshot);
                _sessionJournal.ShowAlarmNotification(
                    alarm,
                    state.ActivatedAt,
                    markUnread,
                    notification.RegisterValueText,
                    state.Context);
                var confirmed = await _dialogService.ShowAlarmNotificationAsync(notification, cancellationToken);
                if (confirmed)
                {
                    var acknowledgement = await _sessionJournal.CompleteAlarmAcknowledgementAsync(
                        state.Context, _bitWriter, cancellationToken);
                    if (acknowledgement is { Succeeded: false })
                    {
                        _logger.LogWarning(
                            "Failed to write acknowledgement for alarm {AlarmId}: {Error}",
                            alarm.Id,
                            acknowledgement.ErrorMessage);
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
                    if (_states.TryGetValue(alarm.Id, out var updated)
                        && ReferenceEquals(updated, state))
                    {
                        updated.IsShowing = false;
                        updated.NextShowAt = now.AddMilliseconds(alarm.RepeatIntervalMs);
                    }
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

        var runtimeOptions = _runtime.CurrentOptions;
        long generation;
        lock (_sync)
        {
            generation = _configurationGeneration;
        }

        Dispatcher.UIThread.Post(() =>
        {
            lock (_sync)
            {
                if (_disposed || generation != _configurationGeneration
                    || !ReferenceEquals(runtimeOptions, _runtime.CurrentOptions))
                {
                    return;
                }
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

    private bool IsCurrent(string alarmId, AlarmRuntimeState state, long generation)
    {
        lock (_sync)
        {
            return !_disposed && generation == state.Context.ConfigurationGeneration
                && _states.TryGetValue(alarmId, out var current) && ReferenceEquals(current, state)
                && ReferenceEquals(state.RuntimeOptions, _runtime.CurrentOptions)
                && state.Role == GetWriteRole(_runtime.Status);
        }
    }

    private static ModbusRuntimeRole? GetWriteRole(ModbusStatus status) =>
        status.ClientState == ModbusConnectionState.Running ? ModbusRuntimeRole.Client
        : status.ServerState == ModbusConnectionState.Running ? ModbusRuntimeRole.Server : null;

    private void OnStatusChanged(object? sender, ModbusStatus status)
    {
        AlarmRuntimeState[] invalidated;
        lock (_sync)
        {
            var next = (status.ClientState, status.ServerState);
            if (_disposed || next == _runtimeStates)
            {
                return;
            }

            _runtimeStates = next;
            invalidated = InvalidateStates();
        }

        CancelStates(invalidated);
    }

    // Called under _sync; cancellation and observable UI updates happen outside it.
    private AlarmRuntimeState[] InvalidateStates(Func<AlarmRuntimeState, bool>? predicate = null)
    {
        _configurationGeneration++;
        var states = _states.Values.Where(state => predicate?.Invoke(state) ?? true).ToArray();
        foreach (var state in states)
        {
            _states.Remove(state.Context.AlarmId);
        }

        return states;
    }

    private void CancelStates(IEnumerable<AlarmRuntimeState> states)
    {
        foreach (var state in states)
        {
            state.Lifetime.Cancel();
            if (Dispatcher.UIThread.CheckAccess())
            {
                _sessionJournal.InvalidateAlarmNotification(state.Context);
            }
            else
            {
                Dispatcher.UIThread.Post(() => _sessionJournal.InvalidateAlarmNotification(state.Context));
            }
        }
    }

    private static bool SameAlarmMap(IReadOnlyList<ModbusAlarmOptions> left, IReadOnlyList<ModbusAlarmOptions> right) =>
        left.Count == right.Count && left.Zip(right).All(pair => SameAlarm(pair.First, pair.Second));

    private static bool SameAlarm(ModbusAlarmOptions left, ModbusAlarmOptions right) =>
        left.Id == right.Id && left.Enabled == right.Enabled && left.Kind == right.Kind
        && left.Message == right.Message && left.RegisterValueEnabled == right.RegisterValueEnabled
        && left.RegisterValuePrefix == right.RegisterValuePrefix && left.RegisterValueAddress == right.RegisterValueAddress
        && SameAddress(left.Alarm, right.Alarm) && SameAddress(left.Acknowledgement, right.Acknowledgement)
        && left.AcknowledgementEnabled == right.AcknowledgementEnabled
        && left.AcknowledgementPulseDurationMs == right.AcknowledgementPulseDurationMs
        && left.RepeatIntervalMs == right.RepeatIntervalMs;

    private static bool SameAddress(ModbusBitAddressOptions left, ModbusBitAddressOptions right) =>
        left.Area == right.Area && left.Address == right.Address && left.BitIndex == right.BitIndex;

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

    private static AlarmNotificationContent CreateNotificationContent(
        ModbusAlarmOptions alarm,
        ModbusSnapshot snapshot)
    {
        if (!alarm.RegisterValueEnabled)
        {
            return new AlarmNotificationContent(alarm.Kind, alarm.Message);
        }

        var valueText = alarm.RegisterValueAddress >= 0
                        && alarm.RegisterValueAddress < snapshot.HoldingRegisters.Count
            ? snapshot.HoldingRegisters[alarm.RegisterValueAddress].ToString(CultureInfo.InvariantCulture)
            : "—";
        return new AlarmNotificationContent(
            alarm.Kind,
            alarm.Message,
            string.Concat(alarm.RegisterValuePrefix, valueText));
    }

    private sealed class AlarmRuntimeState(DateTimeOffset activatedAt, ModbusRuntimeRole role, ModbusOptions runtimeOptions)
    {
        public AlarmAcknowledgementContext Context { get; set; } = null!;
        public CancellationTokenSource Lifetime { get; } = new();
        public ModbusRuntimeRole Role { get; } = role;
        public ModbusOptions RuntimeOptions { get; } = runtimeOptions;
        public bool IsShowing { get; set; }
        public DateTimeOffset? NextShowAt { get; set; }
        public DateTimeOffset ActivatedAt { get; } = activatedAt;
        public bool ShouldMarkNotificationUnread { get; set; } = true;
    }
}
