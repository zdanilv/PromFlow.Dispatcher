using System.Reactive.Threading.Tasks;
using System.Reactive.Linq;
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
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

// Exercise the real panel command and monitor entry point with the same deferred
// dialog. All writes are recorded in memory; no transport is started.
public sealed class NotificationsPanelViewModelTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Confirm_CurrentActivation_RespectsAcknowledgementEnabled(bool panel, bool enabled)
    {
        using var fixture = new Fixture(enabled);
        var pending = await fixture.OpenAsync(panel);

        fixture.Dialog.Answer(0, true);
        await pending;

        Assert.False(Assert.Single(fixture.Journal.Notifications).IsUnread);
        var ok = Assert.Single(fixture.Journal.History, item => item.EventText == "OK");
        Assert.Equal("1", ok.Address);
        Assert.Equal(enabled ? 1 : 0, fixture.Writer.Pulses.Count);
        if (enabled)
        {
            var pulse = Assert.Single(fixture.Writer.Pulses);
            Assert.Equal(ModbusDataArea.Coil, pulse.Area);
            Assert.Equal(1, pulse.Address);
            Assert.Null(pulse.BitIndex);
            Assert.Equal(1, fixture.Writer.Duration);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Close_DoesNotRecordOkOrWrite(bool panel)
    {
        using var fixture = new Fixture();
        var pending = await fixture.OpenAsync(panel);
        fixture.Dialog.Answer(0, false);
        await pending;

        fixture.AssertNoAcknowledgement();
        Assert.True(Assert.Single(fixture.Journal.Notifications).IsUnread);
    }

    [Theory]
    [InlineData(false, "clear")]
    [InlineData(true, "clear")]
    [InlineData(false, "reactivate")]
    [InlineData(true, "reactivate")]
    [InlineData(false, "address")]
    [InlineData(true, "address")]
    [InlineData(false, "feedback")]
    [InlineData(true, "feedback")]
    [InlineData(false, "remove")]
    [InlineData(true, "remove")]
    [InlineData(false, "disable")]
    [InlineData(true, "disable")]
    [InlineData(false, "demo")]
    [InlineData(true, "demo")]
    public async Task Confirm_StaleContext_DoesNotWriteOrReadNewActivation(bool panel, string change)
    {
        using var fixture = new Fixture();
        var pending = await fixture.OpenAsync(panel);
        Task? replacement = null;

        if (change is "clear" or "reactivate")
        {
            await fixture.Monitor.ProcessSnapshotAsync(ModbusAlarmMonitorTests.CreateSnapshot(false), fixture.Now.AddMilliseconds(100));
        }
        else
        {
            var changed = fixture.Options.CurrentValue.Clone();
            switch (change)
            {
                case "address": changed.AlarmMap[0].Acknowledgement.Address = 3; break;
                case "feedback": changed.AlarmMap[0].AcknowledgementEnabled = false; break;
                case "remove": changed.AlarmMap.Clear(); break;
                case "disable": changed.AlarmMap[0].Enabled = false; break;
                case "demo": changed.Client.CoilStartAddress = 100; break;
            }

            fixture.Options.Emit(changed, change == "demo" ? ModbusOptions.DemoSectionName : null);
            if (change is "remove" or "disable")
            {
                fixture.Options.Emit(ModbusAlarmMonitorTests.CreateOptions(1000));
            }
        }

        if (change != "clear")
        {
            replacement = fixture.Monitor.ProcessSnapshotAsync(ModbusAlarmMonitorTests.CreateSnapshot(true), fixture.Now.AddMilliseconds(200));
            Assert.Equal(2, fixture.Dialog.Pending.Count);
        }

        fixture.Dialog.Answer(0, true);
        await pending;

        fixture.AssertNoAcknowledgement();
        Assert.True(Assert.Single(fixture.Journal.Notifications).IsUnread);
        Assert.Equal(change != "clear", Assert.Single(fixture.Journal.Notifications).IsActive);
        if (replacement is not null)
        {
            fixture.Dialog.Answer(1, false);
            await replacement;
        }
    }

    [Fact]
    public async Task OldFinally_DoesNotResetNewDialogsBusyRepeatOrUnread()
    {
        using var fixture = new Fixture();
        var oldDialog = await fixture.OpenAsync(panel: false);
        await fixture.Monitor.ProcessSnapshotAsync(ModbusAlarmMonitorTests.CreateSnapshot(false), fixture.Now.AddMilliseconds(100));
        var newDialog = fixture.Monitor.ProcessSnapshotAsync(ModbusAlarmMonitorTests.CreateSnapshot(true), fixture.Now.AddMilliseconds(200));

        fixture.Dialog.Answer(0, true);
        await oldDialog;
        var whileBusy = fixture.Monitor.ProcessSnapshotAsync(ModbusAlarmMonitorTests.CreateSnapshot(true), fixture.Now.AddMilliseconds(1100));
        Assert.True(whileBusy.IsCompleted, "Old finally must not allow another modal dialog while the new one is busy.");
        await whileBusy;

        Assert.Equal(2, fixture.Dialog.Pending.Count); // The new dialog is still busy.
        fixture.AssertNoAcknowledgement();
        Assert.True(Assert.Single(fixture.Journal.Notifications).IsUnread);

        fixture.Dialog.Answer(1, false);
        await newDialog;
        fixture.Journal.MarkAlarmRead("alarm.main");
        await fixture.Monitor.ProcessSnapshotAsync(ModbusAlarmMonitorTests.CreateSnapshot(true), fixture.Now.AddMilliseconds(1150));
        Assert.Equal(2, fixture.Dialog.Pending.Count); // New activation's repeat deadline is 1200.
        var repeat = fixture.Monitor.ProcessSnapshotAsync(ModbusAlarmMonitorTests.CreateSnapshot(true), fixture.Now.AddMilliseconds(1201));
        Assert.Equal(3, fixture.Dialog.Pending.Count);
        Assert.False(Assert.Single(fixture.Journal.Notifications).IsUnread);
        fixture.Dialog.Answer(2, false);
        await repeat;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Clear_CancelsAckWaitingInWriter_AndCompletionLeavesNewActivationUnread(bool panel)
    {
        using var fixture = new Fixture();
        fixture.Writer.Hold = true;
        var pending = await fixture.OpenAsync(panel);
        fixture.Dialog.Answer(0, true);
        await fixture.Writer.Entered.Task;

        await fixture.Monitor.ProcessSnapshotAsync(ModbusAlarmMonitorTests.CreateSnapshot(false), fixture.Now.AddMilliseconds(100));
        var replacement = fixture.Monitor.ProcessSnapshotAsync(ModbusAlarmMonitorTests.CreateSnapshot(true), fixture.Now.AddMilliseconds(200));
        await pending;

        Assert.Empty(fixture.Writer.Pulses);
        Assert.True(Assert.Single(fixture.Journal.Notifications).IsUnread);
        Assert.Single(fixture.Journal.History, item => item.EventText == "OK"); // Only the original activation.
        fixture.Dialog.Answer(1, false);
        await replacement;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposeMonitor_InvalidatesOpenDialogs(bool panel)
    {
        using var fixture = new Fixture();
        var pending = await fixture.OpenAsync(panel);
        fixture.Monitor.Dispose();
        fixture.Dialog.Answer(0, true);
        await pending;
        fixture.AssertNoAcknowledgement();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_DoesNotRecordOkOrWrite(bool panel)
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        // ReactiveCommand routes task cancellation to ThrownExceptions as well as
        // Execute(). Observe the expected error so its global handler cannot abort
        // another test after this one has already asserted the canceled task.
        var commandError = panel
            ? fixture.Panel.ShowNotificationCommand.ThrownExceptions.Take(1).ToTask()
            : null;
        var pending = await fixture.OpenAsync(panel, cancellation.Token);
        if (panel)
        {
            fixture.Dialog.Pending[0].SetCanceled();
        }
        else
        {
            cancellation.Cancel();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        if (commandError is not null)
        {
            Assert.IsAssignableFrom<OperationCanceledException>(await commandError);
        }

        fixture.AssertNoAcknowledgement();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RuntimeChange_RejectsOldResponse(bool panel, bool endpointChange)
    {
        using var fixture = new Fixture();
        var pending = await fixture.OpenAsync(panel);
        if (endpointChange)
        {
            fixture.Runtime.CurrentOptions = fixture.Runtime.CurrentOptions.Clone();
            fixture.Runtime.CurrentOptions.Client.CoilStartAddress = 100;
        }
        else
        {
            fixture.Runtime.SetStatus(new ModbusStatus { ServerState = ModbusConnectionState.Running });
        }

        fixture.Dialog.Answer(0, true);
        await pending;
        fixture.AssertNoAcknowledgement();

        var snapshot = ModbusAlarmMonitorTests.CreateSnapshot(true);
        var next = fixture.Monitor.ProcessSnapshotAsync(new ModbusSnapshot
        {
            Role = endpointChange ? ModbusRuntimeRole.Client : ModbusRuntimeRole.Server,
            Coils = snapshot.Coils,
            Timestamp = fixture.Now.AddSeconds(2)
        }, fixture.Now.AddSeconds(2));
        Assert.Equal(2, fixture.Dialog.Pending.Count);
        fixture.Dialog.Answer(1, true);
        await next;
        Assert.Single(fixture.Writer.Pulses);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnrelatedMapChange_DoesNotInvalidateCurrentDialog(bool panel)
    {
        using var fixture = new Fixture();
        var pending = await fixture.OpenAsync(panel);
        var changed = fixture.Options.CurrentValue.Clone();
        var another = changed.AlarmMap[0].Clone();
        another.Id = "another.alarm";
        another.Alarm.Address = 2;
        another.Acknowledgement.Address = 3;
        changed.AlarmMap.Add(another);
        fixture.Options.Emit(changed);

        fixture.Dialog.Answer(0, true);
        await pending;

        Assert.Single(fixture.Writer.Pulses);
        Assert.Single(fixture.Journal.History, item => item.EventText == "OK");
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(bool acknowledgementEnabled = true)
        {
            var options = ModbusAlarmMonitorTests.CreateOptions(1000);
            options.AlarmMap[0].AcknowledgementEnabled = acknowledgementEnabled;
            Options = new(options);
            Monitor = new(Options, Runtime, Dialog, Writer, Journal,
                NullLogger<ModbusAlarmMonitor>.Instance);
            Panel = new(Journal, Dialog, Writer);
            Monitor.Start();
        }

        public DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;
        public RouteMapSessionJournal Journal { get; } = new();
        public DeferredDialogService Dialog { get; } = new();
        public GatedBitWriter Writer { get; } = new();
        public ModbusAlarmMonitorTests.NoOpRuntime Runtime { get; } = new();
        public ModbusAlarmMonitorTests.MutableOptionsMonitor Options { get; }
        public ModbusAlarmMonitor Monitor { get; }
        public NotificationsPanelViewModel Panel { get; }

        public async Task<Task> OpenAsync(bool panel, CancellationToken cancellationToken = default)
        {
            if (!panel)
            {
                return Monitor.ProcessSnapshotAsync(ModbusAlarmMonitorTests.CreateSnapshot(true), Now, cancellationToken);
            }

            Dialog.ImmediateAnswer = false;
            await Monitor.ProcessSnapshotAsync(ModbusAlarmMonitorTests.CreateSnapshot(true), Now);
            Dialog.ImmediateAnswer = null;
            return Panel.ShowNotificationCommand.Execute(Assert.Single(Journal.Notifications)).ToTask();
        }

        public void AssertNoAcknowledgement()
        {
            Assert.Empty(Writer.Pulses);
            Assert.DoesNotContain(Journal.History, item => item.EventText == "OK");
        }

        public void Dispose()
        {
            Panel.Dispose();
            Monitor.Dispose();
        }
    }

    private sealed class GatedBitWriter : IModbusBitWriter
    {
        public List<ModbusBitAddressOptions> Pulses { get; } = [];
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Hold { get; set; }
        public int Duration { get; private set; }

        public async Task<ModbusOperationResult> PulseAsync(ModbusBitAddressOptions address, int pulseDurationMs, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            if (Hold)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Duration = pulseDurationMs;
            Pulses.Add(address.Clone());
            return ModbusOperationResult.Success();
        }
    }

    private sealed class DeferredDialogService : IDialogService
    {
        public bool? ImmediateAnswer { get; set; }
        public List<TaskCompletionSource<bool>> Pending { get; } = [];
        public void Answer(int index, bool confirmed) => Pending[index].SetResult(confirmed);

        public Task<bool> ShowAlarmNotificationAsync(ModbusAlarmKind kind, string message, CancellationToken ct = default)
        {
            if (ImmediateAnswer is { } answer)
            {
                return Task.FromResult(answer);
            }

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Add(completion);
            return completion.Task.WaitAsync(ct);
        }

        public Task<bool> ShowAlarmNotificationAsync(AlarmNotificationContent notification, CancellationToken ct = default) =>
            ShowAlarmNotificationAsync(notification.Kind, notification.Message, ct);
        public Task<bool> ConfirmAsync(string message, CancellationToken ct = default) => Task.FromResult(false);
        public Task<string?> RequestSecretAsync(string message, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task ShowErrorAsync(string title, string message, string? details = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ModbusOptions?> EditModbusSettingsAsync(string title, string sectionName, ModbusOptions options, CancellationToken ct = default) => Task.FromResult<ModbusOptions?>(null);
        public Task<OpcUaConfiguredTag?> EditOpcUaTagAsync(string title, OpcUaConfiguredTag? tag, OpcUaImportTarget target, CancellationToken ct = default) => Task.FromResult<OpcUaConfiguredTag?>(null);
        public Task<OpcUaTagImportResult?> ImportOpcUaTagsAsync(OpcUaBrowseRequest request, CancellationToken ct = default) => Task.FromResult<OpcUaTagImportResult?>(null);
    }
}
