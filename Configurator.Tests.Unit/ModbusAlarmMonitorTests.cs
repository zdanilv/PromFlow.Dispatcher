using Configurator.Application.Services.Dialogs;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Desktop.Workspace.Alarms;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Tests.Unit;

public sealed class ModbusAlarmMonitorTests
{
    [Fact]
    public void StartAndDispose_SubscribeAndUnsubscribeOnce()
    {
        var options = new MutableOptionsMonitor(CreateOptions(repeatIntervalMs: 1000));
        var runtime = new RecordingRuntime();
        var monitor = new ModbusAlarmMonitor(
            options,
            runtime,
            new RecordingDialogService(confirm: true),
            new RecordingBitWriter(),
            new RouteMapSessionJournal(),
            NullLogger<ModbusAlarmMonitor>.Instance);

        monitor.Start();
        monitor.Start();

        Assert.Equal(1, runtime.SnapshotSubscriptions);
        Assert.Equal(1, options.ListenerCount);

        monitor.Dispose();
        monitor.Dispose();

        Assert.Equal(0, runtime.SnapshotSubscriptions);
        Assert.Equal(0, options.ListenerCount);
    }

    [Fact]
    public async Task ProcessSnapshotAsync_ShowsOnFrontAndRepeatsAfterInterval()
    {
        var dialog = new RecordingDialogService(confirm: true);
        var writer = new RecordingBitWriter();
        var monitor = CreateMonitor(CreateOptions(repeatIntervalMs: 1000), dialog, writer);
        var now = DateTimeOffset.UtcNow;
        var activeSnapshot = CreateSnapshot(alarmActive: true);

        await monitor.ProcessSnapshotAsync(activeSnapshot, now);
        await monitor.ProcessSnapshotAsync(activeSnapshot, now.AddMilliseconds(500));
        await monitor.ProcessSnapshotAsync(activeSnapshot, now.AddMilliseconds(1001));

        Assert.Equal(2, dialog.AlarmMessages.Count);
        Assert.Equal(2, writer.Pulses.Count);
    }

    [Fact]
    public async Task ProcessSnapshotAsync_ResetFalseAllowsNextFrontImmediately()
    {
        var dialog = new RecordingDialogService(confirm: true);
        var writer = new RecordingBitWriter();
        var monitor = CreateMonitor(CreateOptions(repeatIntervalMs: 60000), dialog, writer);
        var now = DateTimeOffset.UtcNow;

        await monitor.ProcessSnapshotAsync(CreateSnapshot(alarmActive: true), now);
        await monitor.ProcessSnapshotAsync(CreateSnapshot(alarmActive: false), now.AddMilliseconds(100));
        await monitor.ProcessSnapshotAsync(CreateSnapshot(alarmActive: true), now.AddMilliseconds(200));

        Assert.Equal(2, dialog.AlarmMessages.Count);
        Assert.Equal(2, writer.Pulses.Count);
    }

    [Fact]
    public async Task ProcessSnapshotAsync_CloseDoesNotWriteAcknowledgement()
    {
        var dialog = new RecordingDialogService(confirm: false);
        var writer = new RecordingBitWriter();
        var monitor = CreateMonitor(CreateOptions(repeatIntervalMs: 1000), dialog, writer);

        await monitor.ProcessSnapshotAsync(CreateSnapshot(alarmActive: true), DateTimeOffset.UtcNow);

        Assert.Single(dialog.AlarmMessages);
        Assert.Empty(writer.Pulses);
    }

    [Fact]
    public async Task ProcessSnapshotAsync_DisabledAcknowledgementRecordsOkWithoutWritingPulse()
    {
        var options = CreateOptions(repeatIntervalMs: 1000);
        Assert.Single(options.AlarmMap).AcknowledgementEnabled = false;
        var dialog = new RecordingDialogService(confirm: true);
        var writer = new RecordingBitWriter();
        var journal = new RouteMapSessionJournal();
        var monitor = CreateMonitor(options, dialog, writer, journal);

        await monitor.ProcessSnapshotAsync(CreateSnapshot(alarmActive: true), DateTimeOffset.UtcNow);

        Assert.Single(dialog.AlarmMessages);
        Assert.Empty(writer.Pulses);
        Assert.Contains(journal.History, item => item.EventText == "OK");
        Assert.False(Assert.Single(journal.Notifications).IsUnread);
    }

    [Fact]
    public async Task ProcessSnapshotAsync_AfterOptionsChangeUsesNewAlarmMapWithoutRestart()
    {
        var options = new MutableOptionsMonitor(CreateOptions(repeatIntervalMs: 1000));
        var dialog = new RecordingDialogService(confirm: true);
        var writer = new RecordingBitWriter();
        var monitor = new ModbusAlarmMonitor(
            options,
            new RecordingRuntime(),
            dialog,
            writer,
            new RouteMapSessionJournal(),
            NullLogger<ModbusAlarmMonitor>.Instance);
        monitor.Start();
        options.Emit(new ModbusOptions
        {
            AlarmMap =
            [
                new()
                {
                    Id = "alarm.changed",
                    Kind = ModbusAlarmKind.Confirmation,
                    Message = "Повторное подтверждение",
                    Alarm = new ModbusBitAddressOptions
                    {
                        Area = ModbusDataArea.Coil,
                        Address = 2
                    },
                    Acknowledgement = new ModbusBitAddressOptions
                    {
                        Area = ModbusDataArea.Coil,
                        Address = 3
                    },
                    RepeatIntervalMs = 1000,
                    AcknowledgementPulseDurationMs = 1
                }
            ]
        });

        await monitor.ProcessSnapshotAsync(CreateSnapshot(true, false, false, false), DateTimeOffset.UtcNow);
        await monitor.ProcessSnapshotAsync(CreateSnapshot(false, false, true, false), DateTimeOffset.UtcNow.AddMilliseconds(1));

        Assert.Single(dialog.AlarmNotifications);
        Assert.Equal(ModbusAlarmKind.Confirmation, dialog.AlarmNotifications[0].Kind);
        Assert.Equal("Повторное подтверждение", dialog.AlarmNotifications[0].Message);
        var pulse = Assert.Single(writer.Pulses);
        Assert.Equal(3, pulse.Address);
    }

    [Fact]
    public async Task ProcessSnapshotAsync_RoutesAllAlarmKindsToDialog()
    {
        var dialog = new RecordingDialogService(confirm: false);
        var writer = new RecordingBitWriter();
        var monitor = CreateMonitor(new ModbusOptions
        {
            AlarmMap =
            [
                new()
                {
                    Id = "alarm.fault",
                    Kind = ModbusAlarmKind.Fault,
                    Message = "Авария",
                    Alarm = new ModbusBitAddressOptions { Area = ModbusDataArea.Coil, Address = 0 },
                    Acknowledgement = new ModbusBitAddressOptions { Area = ModbusDataArea.Coil, Address = 1 },
                    RepeatIntervalMs = 1000,
                    AcknowledgementPulseDurationMs = 1
                },
                new()
                {
                    Id = "alarm.confirmation",
                    Kind = ModbusAlarmKind.Confirmation,
                    Message = "Повторное подтверждение",
                    Alarm = new ModbusBitAddressOptions { Area = ModbusDataArea.Coil, Address = 2 },
                    Acknowledgement = new ModbusBitAddressOptions { Area = ModbusDataArea.Coil, Address = 3 },
                    RepeatIntervalMs = 1000,
                    AcknowledgementPulseDurationMs = 1
                },
                new()
                {
                    Id = "alarm.message",
                    Kind = ModbusAlarmKind.Message,
                    Message = "Сообщение",
                    Alarm = new ModbusBitAddressOptions { Area = ModbusDataArea.Coil, Address = 4 },
                    Acknowledgement = new ModbusBitAddressOptions { Area = ModbusDataArea.Coil, Address = 5 },
                    RepeatIntervalMs = 1000,
                    AcknowledgementPulseDurationMs = 1
                }
            ]
        }, dialog, writer);

        await monitor.ProcessSnapshotAsync(CreateSnapshot(true, false, true, false, true, false), DateTimeOffset.UtcNow);

        Assert.Equal(
            [ModbusAlarmKind.Fault, ModbusAlarmKind.Confirmation, ModbusAlarmKind.Message],
            dialog.AlarmNotifications.Select(notification => notification.Kind).ToArray());
        Assert.Empty(writer.Pulses);
    }

    [Fact]
    public async Task ProcessSnapshotAsync_AddsSingleNotificationAndMarksReadOnOk()
    {
        var journal = new RouteMapSessionJournal();
        var dialog = new RecordingDialogService(confirm: true);
        var writer = new RecordingBitWriter();
        var monitor = CreateMonitor(CreateOptions(repeatIntervalMs: 1000), dialog, writer, journal);
        var now = DateTimeOffset.UtcNow;
        var activeSnapshot = CreateSnapshot(alarmActive: true);

        await monitor.ProcessSnapshotAsync(activeSnapshot, now);
        await monitor.ProcessSnapshotAsync(activeSnapshot, now.AddMilliseconds(1001));

        var notification = Assert.Single(journal.Notifications);
        Assert.Equal("alarm.main", notification.Id);
        Assert.True(notification.IsActive);
        Assert.False(notification.IsUnread);
        Assert.Equal(2, dialog.AlarmMessages.Count);
        Assert.Equal(2, writer.Pulses.Count);
        Assert.Equal(1, journal.History.Count(item => item.EventText == "Тревога"));
        Assert.Equal(2, journal.History.Count(item => item.EventText == "OK"));
    }

    [Fact]
    public async Task ProcessSnapshotAsync_CapturesConfiguredRegisterValueForDialogAndNotification()
    {
        var options = CreateOptions(repeatIntervalMs: 1000);
        var alarm = Assert.Single(options.AlarmMap);
        alarm.RegisterValueEnabled = true;
        alarm.RegisterValuePrefix = "Температура: ";
        alarm.RegisterValueAddress = 1;
        var journal = new RouteMapSessionJournal();
        var dialog = new RecordingDialogService(confirm: false);
        var monitor = CreateMonitor(options, dialog, new RecordingBitWriter(), journal);
        var snapshot = new ModbusSnapshot
        {
            Role = ModbusRuntimeRole.Client,
            Coils = [true, false],
            HoldingRegisters = [10, 42],
            Timestamp = DateTimeOffset.UtcNow
        };

        await monitor.ProcessSnapshotAsync(snapshot, DateTimeOffset.UtcNow);

        var content = Assert.Single(dialog.AlarmContents);
        Assert.Equal("Температура: 42", content.RegisterValueText);
        var notification = Assert.Single(journal.Notifications);
        Assert.Equal("Температура: 42", notification.RegisterValueText);
        Assert.True(notification.HasRegisterValueText);
    }

    [Fact]
    public async Task ProcessSnapshotAsync_UsesPlaceholderForUnavailableConfiguredRegister()
    {
        var options = CreateOptions(repeatIntervalMs: 1000);
        var alarm = Assert.Single(options.AlarmMap);
        alarm.RegisterValueEnabled = true;
        alarm.RegisterValuePrefix = "Температура: ";
        alarm.RegisterValueAddress = 1;
        var dialog = new RecordingDialogService(confirm: false);
        var monitor = CreateMonitor(options, dialog, new RecordingBitWriter());
        var snapshot = new ModbusSnapshot
        {
            Role = ModbusRuntimeRole.Client,
            Coils = [true, false],
            HoldingRegisters = [10],
            Timestamp = DateTimeOffset.UtcNow
        };

        await monitor.ProcessSnapshotAsync(snapshot, DateTimeOffset.UtcNow);

        Assert.Equal("Температура: —", Assert.Single(dialog.AlarmContents).RegisterValueText);
    }

    [Fact]
    public async Task ProcessSnapshotAsync_DoesNotCreateSecondLineWhenRegisterValueIsDisabled()
    {
        var dialog = new RecordingDialogService(confirm: false);
        var monitor = CreateMonitor(CreateOptions(repeatIntervalMs: 1000), dialog, new RecordingBitWriter());
        var snapshot = new ModbusSnapshot
        {
            Role = ModbusRuntimeRole.Client,
            Coils = [true, false],
            HoldingRegisters = [42],
            Timestamp = DateTimeOffset.UtcNow
        };

        await monitor.ProcessSnapshotAsync(snapshot, DateTimeOffset.UtcNow);

        Assert.Null(Assert.Single(dialog.AlarmContents).RegisterValueText);
    }

    [Fact]
    public async Task ProcessSnapshotAsync_OnlyDismissesNotificationAfterAlarmBitIsFalse()
    {
        var journal = new RouteMapSessionJournal();
        var monitor = CreateMonitor(
            CreateOptions(repeatIntervalMs: 1000),
            new RecordingDialogService(confirm: false),
            new RecordingBitWriter(),
            journal);
        var now = DateTimeOffset.UtcNow;

        await monitor.ProcessSnapshotAsync(CreateSnapshot(alarmActive: true), now);

        Assert.False(journal.TryDismissAlarm("alarm.main"));
        var notification = Assert.Single(journal.Notifications);
        Assert.True(notification.IsUnread);
        Assert.True(notification.IsActive);

        await monitor.ProcessSnapshotAsync(CreateSnapshot(alarmActive: false), now.AddMilliseconds(100));

        Assert.False(notification.IsActive);
        Assert.True(journal.TryDismissAlarm("alarm.main"));
        Assert.Empty(journal.Notifications);
        Assert.Contains(journal.History, item => item.EventText == "Снято");
    }

    private static ModbusAlarmMonitor CreateMonitor(
        ModbusOptions options,
        IDialogService dialog,
        IModbusBitWriter writer,
        RouteMapSessionJournal? journal = null)
        => new(
            new StaticOptionsMonitor(options),
            new NoOpRuntime(),
            dialog,
            writer,
            journal ?? new RouteMapSessionJournal(),
            NullLogger<ModbusAlarmMonitor>.Instance);

    private static ModbusOptions CreateOptions(int repeatIntervalMs)
        => new()
        {
            AlarmMap =
            [
                new()
                {
                    Id = "alarm.main",
                    Kind = ModbusAlarmKind.Fault,
                    Message = "Авария",
                    Alarm = new ModbusBitAddressOptions
                    {
                        Area = ModbusDataArea.Coil,
                        Address = 0
                    },
                    Acknowledgement = new ModbusBitAddressOptions
                    {
                        Area = ModbusDataArea.Coil,
                        Address = 1
                    },
                    RepeatIntervalMs = repeatIntervalMs,
                    AcknowledgementPulseDurationMs = 1
                }
            ]
        };

    private static ModbusSnapshot CreateSnapshot(bool alarmActive)
        => CreateSnapshot(alarmActive, false);

    private static ModbusSnapshot CreateSnapshot(params bool[] coils)
        => new()
        {
            Role = ModbusRuntimeRole.Client,
            Coils = coils,
            HoldingRegisters = [],
            Timestamp = DateTimeOffset.Now
        };

    private sealed class StaticOptionsMonitor(ModbusOptions options) : IOptionsMonitor<ModbusOptions>
    {
        public ModbusOptions CurrentValue { get; } = options;
        public ModbusOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<ModbusOptions, string?> listener) => null;
    }

    private sealed class MutableOptionsMonitor(ModbusOptions options) : IOptionsMonitor<ModbusOptions>
    {
        private readonly List<Action<ModbusOptions, string?>> _listeners = [];
        private ModbusOptions _currentValue = options;

        public int ListenerCount => _listeners.Count;
        public ModbusOptions CurrentValue => _currentValue;
        public ModbusOptions Get(string? name) => _currentValue;

        public IDisposable OnChange(Action<ModbusOptions, string?> listener)
        {
            _listeners.Add(listener);
            return new Subscription(() => _listeners.Remove(listener));
        }

        public void Emit(ModbusOptions options, string? name = null)
        {
            _currentValue = options;
            foreach (var listener in _listeners.ToArray())
            {
                listener(options, name);
            }
        }

        private sealed class Subscription(Action dispose) : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                dispose();
            }
        }
    }

    private sealed class RecordingDialogService(bool confirm) : IDialogService
    {
        public List<string> AlarmMessages { get; } = [];
        public List<(ModbusAlarmKind Kind, string Message)> AlarmNotifications { get; } = [];
        public List<AlarmNotificationContent> AlarmContents { get; } = [];
        public Task<bool> ConfirmAsync(string message, CancellationToken ct = default) => Task.FromResult(false);
        public Task<string?> RequestSecretAsync(string message, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task ShowErrorAsync(string title, string message, string? details = null, CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> ShowAlarmNotificationAsync(ModbusAlarmKind kind, string message, CancellationToken ct = default)
        {
            AlarmMessages.Add(message);
            AlarmNotifications.Add((kind, message));
            return Task.FromResult(confirm);
        }

        public Task<bool> ShowAlarmNotificationAsync(AlarmNotificationContent notification, CancellationToken ct = default)
        {
            AlarmMessages.Add(notification.Message);
            AlarmNotifications.Add((notification.Kind, notification.Message));
            AlarmContents.Add(notification);
            return Task.FromResult(confirm);
        }

        public Task<ModbusOptions?> EditModbusSettingsAsync(string title, string sectionName, ModbusOptions options, CancellationToken ct = default) => Task.FromResult<ModbusOptions?>(null);
        public Task<OpcUaConfiguredTag?> EditOpcUaTagAsync(string title, OpcUaConfiguredTag? tag, OpcUaImportTarget target, CancellationToken ct = default) => Task.FromResult<OpcUaConfiguredTag?>(null);
        public Task<OpcUaTagImportResult?> ImportOpcUaTagsAsync(OpcUaBrowseRequest request, CancellationToken ct = default) => Task.FromResult<OpcUaTagImportResult?>(null);
    }

    private sealed class RecordingBitWriter : IModbusBitWriter
    {
        public List<ModbusBitAddressOptions> Pulses { get; } = [];

        public Task<ModbusOperationResult> PulseAsync(
            ModbusBitAddressOptions address,
            int pulseDurationMs,
            CancellationToken cancellationToken = default)
        {
            Pulses.Add(address.Clone());
            return Task.FromResult(ModbusOperationResult.Success());
        }
    }

    private sealed class NoOpRuntime : IModbusRuntimeService
    {
        public ModbusStatus Status { get; } = new()
        {
            ClientState = ModbusConnectionState.Running,
            ServerState = ModbusConnectionState.Stopped
        };
        public ModbusSnapshot ClientSnapshot => ModbusSnapshot.Empty;
        public ModbusSnapshot ServerSnapshot => ModbusSnapshot.Empty;
        public ModbusOptions CurrentOptions { get; } = new();
        public event EventHandler<ModbusStatus>? StatusChanged { add { } remove { } }
        public event EventHandler<ModbusSnapshot>? SnapshotChanged { add { } remove { } }
        public Task StartAsync(ModbusRunMode mode = ModbusRunMode.Both, ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestartAsync(ModbusRunMode mode = ModbusRunMode.Both, ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopClientAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopServerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingRuntime : IModbusRuntimeService
    {
        public int SnapshotSubscriptions { get; private set; }
        public ModbusStatus Status { get; } = new()
        {
            ClientState = ModbusConnectionState.Running,
            ServerState = ModbusConnectionState.Stopped
        };
        public ModbusSnapshot ClientSnapshot => ModbusSnapshot.Empty;
        public ModbusSnapshot ServerSnapshot => ModbusSnapshot.Empty;
        public ModbusOptions CurrentOptions { get; } = new();
        public event EventHandler<ModbusStatus>? StatusChanged { add { } remove { } }
        public event EventHandler<ModbusSnapshot>? SnapshotChanged
        {
            add => SnapshotSubscriptions++;
            remove => SnapshotSubscriptions--;
        }
        public Task StartAsync(ModbusRunMode mode = ModbusRunMode.Both, ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestartAsync(ModbusRunMode mode = ModbusRunMode.Both, ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopClientAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopServerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
