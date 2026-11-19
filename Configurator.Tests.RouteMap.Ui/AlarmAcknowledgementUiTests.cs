using System.Reactive.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Configurator.Application.Services.Dialogs;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Desktop.Workspace.Alarms;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Tests.RouteMap.Ui;

public sealed class AlarmAcknowledgementUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Panel_BackgroundDialogResponse_UpdatesJournalOnUiThread(bool enabled)
    {
        var alarm = CreateAlarm();
        alarm.AcknowledgementEnabled = enabled;
        var journal = new RouteMapSessionJournal();
        var context = new AlarmAcknowledgementContext(alarm, 1, 1, () => true, CancellationToken.None);
        journal.ShowAlarmNotification(alarm, DateTimeOffset.UtcNow, true, null, context);
        var dialog = new DeferredDialogs();
        var writer = new RecordingWriter();
        using var panel = new NotificationsPanelViewModel(journal, dialog, writer);
        var changesOnUiThread = new List<bool>();
        journal.Notifications[0].PropertyChanged += (_, _) => changesOnUiThread.Add(Dispatcher.UIThread.CheckAccess());
        journal.History.CollectionChanged += (_, _) => changesOnUiThread.Add(Dispatcher.UIThread.CheckAccess());

        var pending = panel.ShowNotificationCommand.Execute(journal.Notifications[0]).ToTask();
        await Task.Run(() => dialog.Response.SetResult(true));
        await pending;

        Assert.NotEmpty(changesOnUiThread);
        Assert.All(changesOnUiThread, onUi => Assert.True(onUi));
        Assert.False(journal.Notifications[0].IsUnread);
        Assert.Single(journal.History, item => item.EventText == "OK");
        Assert.Equal(enabled ? 1 : 0, writer.Count);
    }

    [AvaloniaFact]
    public async Task BackgroundReload_InvalidatesOldContextOnUiThread_WithoutDeactivatingReplacement()
    {
        var options = new MutableOptions(new ModbusOptions { AlarmMap = [CreateAlarm()] });
        var journal = new RouteMapSessionJournal();
        var writer = new RecordingWriter();
        using var monitor = new ModbusAlarmMonitor(options, new LocalRuntime(), new ClosedDialogs(), writer,
            journal, NullLogger<ModbusAlarmMonitor>.Instance);
        monitor.Start();
        var snapshot = new ModbusSnapshot { Role = ModbusRuntimeRole.Client, Coils = [true, false, false, false] };
        await monitor.ProcessSnapshotAsync(snapshot, DateTimeOffset.UtcNow);
        var old = Assert.Single(journal.Notifications).AcknowledgementContext!;
        var uiChanges = new List<bool>();
        journal.Notifications[0].PropertyChanged += (_, _) => uiChanges.Add(Dispatcher.UIThread.CheckAccess());

        var changed = options.CurrentValue.Clone();
        changed.AlarmMap[0].Acknowledgement.Address = 3;
        // Do not pump the dispatcher until a replacement exists: the queued
        // invalidation of the old notification must not deactivate the new one.
        Task.Run(() => options.Emit(changed)).GetAwaiter().GetResult();
        Assert.False(old.IsCurrent());
        await monitor.ProcessSnapshotAsync(snapshot, DateTimeOffset.UtcNow.AddSeconds(1));
        Dispatcher.UIThread.RunJobs();

        var item = Assert.Single(journal.Notifications);
        Assert.True(item.IsActive);
        Assert.True(item.IsUnread);
        Assert.NotSame(old, item.AcknowledgementContext);
        Assert.NotEqual(old.ActivationToken, item.AcknowledgementContext!.ActivationToken);
        Assert.NotEqual(old.ConfigurationGeneration, item.AcknowledgementContext.ConfigurationGeneration);
        await journal.CompleteAlarmAcknowledgementAsync(old, writer);
        Assert.Equal(0, writer.Count);
        Assert.DoesNotContain(journal.History, entry => entry.EventText == "OK");
        Assert.All(uiChanges, onUi => Assert.True(onUi));
        await journal.CompleteAlarmAcknowledgementAsync(item.AcknowledgementContext, writer);
        Assert.Equal(1, writer.Count);
        Assert.Equal(3, writer.Address);
    }

    private static ModbusAlarmOptions CreateAlarm() => new()
    {
        Id = "alarm.ui", Message = "Тревога", RepeatIntervalMs = 1000,
        Alarm = new() { Area = ModbusDataArea.Coil, Address = 0 },
        Acknowledgement = new() { Area = ModbusDataArea.Coil, Address = 1 },
        AcknowledgementPulseDurationMs = 1
    };

    private sealed class RecordingWriter : IModbusBitWriter
    {
        public int Count { get; private set; }
        public int Address { get; private set; }
        public Task<ModbusOperationResult> PulseAsync(ModbusBitAddressOptions address, int pulseDurationMs, CancellationToken cancellationToken = default)
        {
            Assert.True(Dispatcher.UIThread.CheckAccess());
            Count++;
            Address = address.Address;
            return Task.FromResult(ModbusOperationResult.Success());
        }
    }

    private sealed class MutableOptions(ModbusOptions options) : IOptionsMonitor<ModbusOptions>
    {
        private Action<ModbusOptions, string?>? _listener;
        public ModbusOptions CurrentValue { get; private set; } = options;
        public ModbusOptions Get(string? name) => CurrentValue;
        public IDisposable OnChange(Action<ModbusOptions, string?> listener)
        {
            _listener = listener;
            return new Subscription(() => _listener = null);
        }
        public void Emit(ModbusOptions value)
        {
            CurrentValue = value;
            _listener?.Invoke(value, null);
        }
        private sealed class Subscription(Action dispose) : IDisposable { public void Dispose() => dispose(); }
    }

    private class ClosedDialogs : IDialogService
    {
        public virtual Task<bool> ShowAlarmNotificationAsync(ModbusAlarmKind kind, string message, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> ConfirmAsync(string message, CancellationToken ct = default) => Task.FromResult(false);
        public Task<string?> RequestSecretAsync(string message, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task ShowErrorAsync(string title, string message, string? details = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ModbusOptions?> EditModbusSettingsAsync(string title, string sectionName, ModbusOptions options, CancellationToken ct = default) => Task.FromResult<ModbusOptions?>(null);
        public Task<OpcUaConfiguredTag?> EditOpcUaTagAsync(string title, OpcUaConfiguredTag? tag, OpcUaImportTarget target, CancellationToken ct = default) => Task.FromResult<OpcUaConfiguredTag?>(null);
        public Task<OpcUaTagImportResult?> ImportOpcUaTagsAsync(OpcUaBrowseRequest request, CancellationToken ct = default) => Task.FromResult<OpcUaTagImportResult?>(null);
    }

    private sealed class DeferredDialogs : ClosedDialogs
    {
        public TaskCompletionSource<bool> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task<bool> ShowAlarmNotificationAsync(ModbusAlarmKind kind, string message, CancellationToken ct = default) => Response.Task;
    }

    private sealed class LocalRuntime : IModbusRuntimeService
    {
        public ModbusStatus Status { get; } = new() { ClientState = ModbusConnectionState.Running };
        public ModbusOptions CurrentOptions { get; } = new();
        public ModbusSnapshot ClientSnapshot => ModbusSnapshot.Empty;
        public ModbusSnapshot ServerSnapshot => ModbusSnapshot.Empty;
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
}
