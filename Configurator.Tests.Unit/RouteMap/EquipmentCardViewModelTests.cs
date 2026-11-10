using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;
using System.Reactive.Threading.Tasks;
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class EquipmentCardViewModelTests
{
    [Theory]
    [InlineData(0, "Выключен", "#77828D", true)]
    [InlineData(1, "Ожидание", "#D99B22", true)]
    [InlineData(2, "Авария", "#D95D4E", false)]
    [InlineData(3, "Выполнение", "#3A9D5D", true)]
    [InlineData(4, "Выгрузка", "#3A9D5D", true)]
    [InlineData(5, "Загрузка", "#3A9D5D", true)]
    public async Task Applies_structured_status_and_enforces_command_availability(
        ushort code, string text, string color, bool canCommand)
    {
        var dispatcher = new CapturingEquipmentCommandDispatcher();
        var card = RouteMapSeed.Create().MapEquipment.Single();
        var viewModel = new EquipmentCardViewModel(card, dispatcher);

        Assert.False(viewModel.CanStart);
        Assert.False(viewModel.CanStop);
        Assert.Equal("Нет достоверного статуса", viewModel.StatusText);

        viewModel.ApplyRuntime(BucketRuntime(code));

        Assert.Equal(text, viewModel.StatusText);
        Assert.Equal(Avalonia.Media.Color.Parse(color), BrushColor(viewModel.StatusBrush));
        Assert.Equal(code, viewModel.StatusPresentation?.Code);
        Assert.Equal(canCommand, viewModel.CanStart);
        Assert.Equal(canCommand, viewModel.CanStop);
        Assert.Contains("equip.bucket.text", viewModel.StatusToolTip);
        Assert.Empty(dispatcher.Requests);

        if (!canCommand)
        {
            await viewModel.StartCommand.Execute().ToTask();
            viewModel.IsStopChecked = true;
            Assert.Empty(dispatcher.Requests);
        }
    }

    [Theory]
    [InlineData(EquipmentStatusQuality.UnknownCode, "Неизвестный статус (код 9)", "#D99B22")]
    [InlineData(EquipmentStatusQuality.Missing, "Нет достоверного статуса", "#77828D")]
    [InlineData(EquipmentStatusQuality.BadOrStale, "Нет достоверного статуса", "#77828D")]
    [InlineData(EquipmentStatusQuality.WrongType, "Нет достоверного статуса", "#77828D")]
    [InlineData(EquipmentStatusQuality.Disconnected, "Не в сети", "#3F474D")]
    [InlineData(EquipmentStatusQuality.ForcedFault, "Авария системы", "#D95D4E")]
    public void Status_replaces_previous_color_and_recovers_on_good_snapshot(
        EquipmentStatusQuality quality, string expectedText, string expectedColor)
    {
        var dispatcher = new CapturingEquipmentCommandDispatcher();
        var definition = RouteMapSeed.Create();
        var card = definition.MapEquipment.Single();
        var viewModel = new EquipmentCardViewModel(card, dispatcher);
        var mapper = new RouteMapRuntimeMapper(definition);
        const string textId = "equip.bucket.text";
        const string powerId = "equip.bucket.power";
        var signals = new Dictionary<string, SignalValue>
        {
            [textId] = new(textId, (ushort)3, SignalValueType.UInt16, DateTimeOffset.UnixEpoch, true, false),
            [powerId] = new(powerId, true, SignalValueType.Bool, DateTimeOffset.UnixEpoch, true, false)
        };
        var goodText = signals[textId];
        viewModel.ApplyRuntime(mapper.Map(signals).Find(card.Id));
        Assert.Equal("Выполнение", viewModel.StatusText);

        switch (quality)
        {
            case EquipmentStatusQuality.UnknownCode:
                signals[textId] = goodText with { Value = (ushort)9 };
                break;
            case EquipmentStatusQuality.Missing:
                signals.Remove(textId);
                break;
            case EquipmentStatusQuality.BadOrStale:
                signals[textId] = goodText with { IsStale = true };
                break;
            case EquipmentStatusQuality.WrongType:
                signals[textId] = goodText with { Value = "3" };
                break;
            case EquipmentStatusQuality.Disconnected:
                var connectionId = RouteMapSystemSignalIds.ConnectionConnected;
                signals[connectionId] = new(connectionId, false, SignalValueType.Bool, DateTimeOffset.UnixEpoch, true, false);
                break;
            case EquipmentStatusQuality.ForcedFault:
                var faultId = RouteMapSystemSignalIds.GlobalFault;
                signals[faultId] = new(faultId, true, SignalValueType.Bool, DateTimeOffset.UnixEpoch, true, false);
                break;
        }

        var runtime = mapper.Map(signals);
        viewModel.ApplyRuntime(runtime.Find(card.Id), runtime.IsConnectionAvailable);

        Assert.Equal(expectedText, viewModel.StatusText);
        Assert.Equal(Avalonia.Media.Color.Parse(expectedColor), BrushColor(viewModel.StatusBrush));
        Assert.Equal(quality, viewModel.StatusPresentation?.Quality);
        Assert.False(viewModel.CanStart);
        Assert.False(viewModel.CanStop);
        Assert.Equal(Avalonia.Media.Color.Parse(runtime.IsConnectionAvailable ? "#00D107" : "#3F474D"),
            BrushColor(viewModel.PowerIndicatorBackground));

        viewModel.ApplyRuntime(BucketRuntime(3));
        Assert.Equal("Выполнение", viewModel.StatusText);
        Assert.Equal(Avalonia.Media.Color.Parse("#3A9D5D"), BrushColor(viewModel.StatusBrush));
        Assert.True(viewModel.CanStart);
        Assert.True(viewModel.CanStop);
        Assert.Empty(dispatcher.Requests);
    }

    [Fact]
    public void Mapped_toggle_start_respects_status_gate_and_applies_readback_without_writes()
    {
        var dispatcher = new CapturingEquipmentCommandDispatcher();
        var card = RouteMapSeed.Create().MapEquipment.Single() with { StartButtonKind = RouteCommandButtonKind.Toggle };
        var viewModel = new EquipmentCardViewModel(card, dispatcher);
        viewModel.ApplyRuntime(BucketRuntime(2));

        viewModel.IsStartChecked = true;

        Assert.False(viewModel.IsStartChecked);
        Assert.Empty(dispatcher.Requests);
        viewModel.ApplyRuntime(BucketRuntime(2) with { IsStartChecked = true });
        Assert.True(viewModel.IsStartChecked);
        Assert.Empty(dispatcher.Requests);

        viewModel.ApplyRuntime(BucketRuntime(3));
        viewModel.IsStartChecked = true;
        Assert.Equal(("equip.bucket.start", true), (Assert.Single(dispatcher.Requests).SignalId, dispatcher.Requests[0].Value));
    }

    [Fact]
    public void Legacy_selector_reset_is_not_replayed_when_configuration_is_applied()
    {
        var dispatcher = new CapturingEquipmentCommandDispatcher();
        var card = RouteMapSeed.Create().MapEquipment.Single() with
        {
            StatusMapping = null,
            Bindings = [
                new(SignalBindingRole.CheckedCommand, "legacy.on", SignalBindingDirection.ReadWrite, SignalValueType.Bool),
                new(SignalBindingRole.UncheckedCommand, "legacy.off", SignalBindingDirection.ReadWrite, SignalValueType.Bool)]
        };
        var viewModel = new EquipmentCardViewModel(card, dispatcher);
        var disabled = new RouteObjectRuntimeState(card.Id, RouteObjectState.Disabled, null, null,
            true, false, false, IsEnabled: false, ShouldResetSelectorCommands: true);

        viewModel.ApplyRuntime(disabled, isConnectionAvailable: true, allowSelectorReset: false);
        viewModel.ApplyRuntime(disabled);
        Assert.Empty(dispatcher.Requests);

        viewModel.ApplyRuntime(disabled with { State = RouteObjectState.Idle, IsEnabled = true, ShouldResetSelectorCommands = false });
        viewModel.ApplyRuntime(disabled);
        Assert.Collection(dispatcher.Requests,
            request => Assert.Equal(("legacy.on", false), (request.SignalId, request.Value)),
            request => Assert.Equal(("legacy.off", false), (request.SignalId, request.Value)));
    }

    [Fact]
    public void ApplyRuntime_updates_checked_state_without_dispatching_commands()
    {
        var dispatcher = new CapturingEquipmentCommandDispatcher();
        var card = RouteMapSeed.Create().MapEquipment.Single();
        var viewModel = new EquipmentCardViewModel(card, dispatcher);

        viewModel.ApplyRuntime(new RouteObjectRuntimeState(
            card.Id,
            RouteObjectState.Idle,
            "Выключено",
            ValueText: null,
            IsVisible: true,
            CanStart: true,
            CanStop: true,
            IsStartChecked: true,
            IsStopChecked: true));

        Assert.False(viewModel.IsStartChecked);
        Assert.True(viewModel.IsStopChecked);
        Assert.Empty(dispatcher.Requests);
    }

    [Fact]
    public async Task Start_button_dispatches_one_true_without_changing_stop()
    {
        var dispatcher = new CapturingEquipmentCommandDispatcher();
        var card = RouteMapSeed.Create().MapEquipment.Single();
        var viewModel = new EquipmentCardViewModel(card, dispatcher);
        viewModel.ApplyRuntime(BucketRuntime());

        await viewModel.StartCommand.Execute().ToTask();
        viewModel.IsStartChecked = true;

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(("equip.bucket.start", true, SignalValueType.Bool), (request.SignalId, request.Value, request.ValueType));
        Assert.False(viewModel.IsStartChecked);
    }

    [Fact]
    public void Stop_toggle_sends_requested_values_only_after_readback()
    {
        var dispatcher = new CapturingEquipmentCommandDispatcher();
        var card = RouteMapSeed.Create().MapEquipment.Single();
        var viewModel = new EquipmentCardViewModel(card, dispatcher);
        viewModel.ApplyRuntime(BucketRuntime());

        viewModel.IsStopChecked = true;
        Assert.False(viewModel.IsStopChecked);
        Assert.True(viewModel.IsStopPending);
        viewModel.ApplyRuntime(BucketRuntime(isStopChecked: true));
        Assert.True(viewModel.IsStopChecked);
        Assert.False(viewModel.IsStopPending);
        viewModel.IsStopChecked = false;

        Assert.Collection(dispatcher.Requests,
            request => Assert.Equal(("equip.bucket.stop", true, SignalValueType.Bool), (request.SignalId, request.Value, request.ValueType)),
            request => Assert.Equal(("equip.bucket.stop", false, SignalValueType.Bool), (request.SignalId, request.Value, request.ValueType)));
        Assert.True(viewModel.IsStopChecked);
    }

    [Fact]
    public async Task Start_is_busy_until_dispatch_finishes_and_does_not_overlap()
    {
        var dispatcher = new BlockingEquipmentCommandDispatcher();
        var viewModel = new EquipmentCardViewModel(RouteMapSeed.Create().MapEquipment.Single(), dispatcher);
        viewModel.ApplyRuntime(BucketRuntime());

        var first = viewModel.StartCommand.Execute().ToTask();
        Assert.True(viewModel.IsStartBusy);
        Assert.False(viewModel.CanStart);
        Assert.Single(dispatcher.Requests);

        dispatcher.Complete();
        await first;

        Assert.False(viewModel.IsStartBusy);
        Assert.True(viewModel.CanStart);
        Assert.Single(dispatcher.Requests);
    }

    [Fact]
    public async Task Stop_stays_pending_until_write_and_readback_both_complete()
    {
        var dispatcher = new BlockingEquipmentCommandDispatcher();
        var card = RouteMapSeed.Create().MapEquipment.Single();
        var viewModel = new EquipmentCardViewModel(card, dispatcher);
        viewModel.ApplyRuntime(BucketRuntime());

        viewModel.IsStopChecked = true;
        Assert.True(viewModel.IsStopPending);
        viewModel.ApplyRuntime(BucketRuntime(isStopChecked: true));
        Assert.True(viewModel.IsStopChecked);
        Assert.True(viewModel.IsStopPending);
        Assert.False(viewModel.CanStop);

        dispatcher.Complete();
        await dispatcher.Completed;
        Assert.True(SpinWait.SpinUntil(() => !viewModel.IsStopPending, TimeSpan.FromSeconds(1)));
        Assert.False(viewModel.IsStopPending);
        Assert.True(viewModel.CanStop);
        Assert.Single(dispatcher.Requests);
    }

    [Fact]
    public async Task Command_errors_are_visible_and_stop_state_waits_for_readback()
    {
        var dispatcher = new FailingEquipmentCommandDispatcher();
        var viewModel = new EquipmentCardViewModel(RouteMapSeed.Create().MapEquipment.Single(), dispatcher);
        viewModel.ApplyRuntime(BucketRuntime());

        await viewModel.StartCommand.Execute().ToTask();
        Assert.True(viewModel.HasCommandError);
        Assert.Contains("ПУСК", viewModel.CommandErrorMessage);
        Assert.False(viewModel.IsStartBusy);

        viewModel.IsStopChecked = true;
        Assert.True(viewModel.HasCommandError);
        Assert.Contains("СТОП", viewModel.CommandErrorMessage);
        Assert.False(viewModel.IsStopChecked);
        Assert.False(viewModel.IsStopPending);
        Assert.Equal(2, dispatcher.Requests.Count);
    }

    [Fact]
    public async Task Momentary_start_and_toggle_stop_require_their_write_modes()
    {
        var dispatcher = new CapturingEquipmentCommandDispatcher();
        var viewModel = new EquipmentCardViewModel(RouteMapSeed.Create().MapEquipment.Single(), dispatcher);
        viewModel.ApplyRuntime(BucketRuntime());

        await viewModel.StartCommand.Execute().ToTask();
        viewModel.IsStopChecked = true;

        Assert.Collection(dispatcher.Requests,
            request => Assert.Equal(Configurator.Application.Services.Modbus.Configuration.ModbusWriteMode.Pulse, request.RequiredWriteMode),
            request => Assert.Equal(Configurator.Application.Services.Modbus.Configuration.ModbusWriteMode.Latched, request.RequiredWriteMode));
    }

    [Fact]
    public async Task Stop_without_matching_readback_times_out_and_reports_error()
    {
        var dispatcher = new CapturingEquipmentCommandDispatcher();
        var card = RouteMapSeed.Create().MapEquipment.Single();
        var viewModel = new EquipmentCardViewModel(card, dispatcher,
            stopConfirmationTimeout: TimeSpan.FromMilliseconds(20));
        viewModel.ApplyRuntime(BucketRuntime());

        viewModel.IsStopChecked = true;
        viewModel.ApplyRuntime(BucketRuntime());

        Assert.True(SpinWait.SpinUntil(() => !viewModel.IsStopPending, TimeSpan.FromSeconds(2)));
        Assert.False(viewModel.IsStopChecked);
        Assert.Contains("СТОП не подтверждён", viewModel.CommandErrorMessage);
        viewModel.ApplyRuntime(BucketRuntime(code: 3, isConnected: false), isConnectionAvailable: false);
        viewModel.ApplyRuntime(BucketRuntime(code: 1));
        Assert.True(viewModel.CanStop);
        Assert.False(viewModel.IsStopChecked);
        Assert.Single(dispatcher.Requests);
    }

    [Fact]
    public void Stop_release_waits_for_fresh_readback_through_status_and_connection_changes()
    {
        const string stopId = "equip.bucket.stop";
        const string offId = "test.bucket.stop-off";
        var seed = RouteMapSeed.Create();
        var bucket = seed.MapEquipment.Single();
        var card = bucket with
        {
            Bindings = [..bucket.Bindings, new(SignalBindingRole.StopOffFeedback, offId,
                SignalBindingDirection.Read, SignalValueType.Bool)]
        };
        var mapper = new RouteMapRuntimeMapper(seed with { MapEquipment = [card] });
        var dispatcher = new CapturingEquipmentCommandDispatcher();
        var viewModel = new EquipmentCardViewModel(card, dispatcher);

        void Publish(ushort code, bool? stop, bool isGood = true, bool isStale = false,
            bool isConnected = true, bool isOff = false)
        {
            var connectionId = RouteMapSystemSignalIds.ConnectionConnected;
            var snapshot = new Dictionary<string, SignalValue>
            {
                ["equip.bucket.text"] = new("equip.bucket.text", code, SignalValueType.UInt16, DateTimeOffset.UnixEpoch, true, false),
                [connectionId] = new(connectionId, isConnected, SignalValueType.Bool, DateTimeOffset.UnixEpoch, true, false),
                [offId] = new(offId, isOff, SignalValueType.Bool, DateTimeOffset.UnixEpoch, true, false),
            };
            if (stop.HasValue)
                snapshot[stopId] = new(stopId, stop.Value, SignalValueType.Bool, DateTimeOffset.UnixEpoch, isGood, isStale);
            var runtime = mapper.Map(snapshot);
            viewModel.ApplyRuntime(runtime.Find(card.Id), runtime.IsConnectionAvailable);
        }

        Publish(3, stop: true);
        Assert.True(viewModel.IsStopChecked);
        viewModel.RequestStopChange(false);
        Assert.True(viewModel.IsStopPending);

        Publish(2, stop: null);
        Assert.True(viewModel.IsStopPending);
        Publish(4, stop: false, isGood: false);
        Assert.True(viewModel.IsStopPending);
        Publish(5, stop: false, isStale: true);
        Assert.True(viewModel.IsStopPending);
        Publish(3, stop: false, isConnected: false);
        Assert.True(viewModel.IsStopPending);
        Publish(3, stop: true, isOff: true);
        Assert.False(viewModel.IsStopChecked);
        Assert.True(viewModel.IsStopPending);

        Publish(1, stop: false);
        Assert.False(viewModel.IsStopChecked);
        Assert.False(viewModel.IsStopPending);
        Assert.True(viewModel.CanStop);
        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(stopId, request.SignalId);
        Assert.Equal(false, request.Value);
    }

    [Fact]
    public void Power_indicator_uses_only_fresh_runtime_readback()
    {
        var dispatcher = new CapturingEquipmentCommandDispatcher();
        var card = RouteMapSeed.Create().MapEquipment.Single();
        var viewModel = new EquipmentCardViewModel(card, dispatcher);
        RouteObjectRuntimeState Runtime(bool? value, RouteObjectState state = RouteObjectState.Idle) =>
            new(card.Id, state, card.StatusText, null, true, true, true, IsPowerOn: value);

        Assert.Equal(Avalonia.Media.Color.Parse("#3F474D"), BrushColor(viewModel.PowerIndicatorBackground));
        viewModel.ApplyRuntime(Runtime(false));
        Assert.Equal(Avalonia.Media.Color.Parse("#D10000"), BrushColor(viewModel.PowerIndicatorBackground));
        viewModel.ApplyRuntime(Runtime(true));
        Assert.Equal(Avalonia.Media.Color.Parse("#00D107"), BrushColor(viewModel.PowerIndicatorBackground));
        viewModel.ApplyRuntime(Runtime(null));
        Assert.Equal(Avalonia.Media.Color.Parse("#3F474D"), BrushColor(viewModel.PowerIndicatorBackground));
        viewModel.ApplyRuntime(Runtime(true, RouteObjectState.Offline));
        Assert.Equal(Avalonia.Media.Color.Parse("#3F474D"), BrushColor(viewModel.PowerIndicatorBackground));
        viewModel.ApplyRuntime(Runtime(true));
        viewModel.ApplyRuntime(null, isConnectionAvailable: false);
        Assert.Equal(Avalonia.Media.Color.Parse("#3F474D"), BrushColor(viewModel.PowerIndicatorBackground));
        Assert.Empty(dispatcher.Requests);
    }

    [Fact]
    public void Selector_without_seed_bindings_does_not_dispatch_commands()
    {
        var dispatcher = new CapturingEquipmentCommandDispatcher();
        var card = RouteMapSeed.Create().MapEquipment.Single();
        var viewModel = new EquipmentCardViewModel(card, dispatcher);

        viewModel.IsSelectorChecked = true;
        viewModel.IsSelectorChecked = false;

        Assert.Empty(dispatcher.Requests);
    }

    [Fact]
    public void Parameters_button_in_user_mode_is_available_without_connection()
    {
        var card = RouteMapSeed.Create().MapEquipment.Single();
        var viewModel = new EquipmentCardViewModel(card, new CapturingEquipmentCommandDispatcher());
        var disabledRuntime = new RouteObjectRuntimeState(
            card.Id,
            RouteObjectState.Disabled,
            card.StatusText,
            ValueText: null,
            IsVisible: true,
            CanStart: false,
            CanStop: false,
            IsEnabled: false);

        Assert.True(viewModel.IsParametersButtonEnabled);

        viewModel.ApplyRuntime(disabledRuntime, isConnectionAvailable: true);

        Assert.False(viewModel.IsEnabled);
        Assert.True(viewModel.IsParametersButtonEnabled);
        Assert.Equal(Avalonia.Media.Color.Parse("#D0D0D0"), BrushColor(viewModel.ParameterBackground));

        viewModel.ApplyRuntime(disabledRuntime, isConnectionAvailable: false);

        Assert.True(viewModel.IsParametersButtonEnabled);
        Assert.Equal(Avalonia.Media.Color.Parse("#D0D0D0"), BrushColor(viewModel.ParameterBackground));
    }

    [Fact]
    public void Parameters_button_in_admin_mode_is_available_before_and_without_connection()
    {
        var card = RouteMapSeed.Create().MapEquipment.Single();
        var viewModel = new EquipmentCardViewModel(card, new CapturingEquipmentCommandDispatcher());
        var offlineRuntime = new RouteObjectRuntimeState(
            card.Id,
            RouteObjectState.Offline,
            "Не в сети",
            ValueText: null,
            IsVisible: true,
            CanStart: false,
            CanStop: false,
            IsEnabled: false);

        Assert.True(viewModel.IsParametersButtonEnabled);

        viewModel.ApplyRuntime(offlineRuntime, isConnectionAvailable: false);

        Assert.False(viewModel.IsEnabled);
        Assert.True(viewModel.IsParametersButtonEnabled);
        Assert.Equal(Avalonia.Media.Color.Parse("#D0D0D0"), BrushColor(viewModel.ParameterBackground));
    }

    [Fact]
    public void ApplyRuntime_keeps_selector_enabled_when_card_is_disabled_without_reset_request()
    {
        var dispatcher = new CapturingEquipmentCommandDispatcher();
        var card = RouteMapSeed.Create().MapEquipment.Single();
        var viewModel = new EquipmentCardViewModel(card, dispatcher);

        viewModel.ApplyRuntime(new RouteObjectRuntimeState(
            card.Id,
            RouteObjectState.Disabled,
            card.StatusText,
            ValueText: null,
            IsVisible: true,
            CanStart: false,
            CanStop: false,
            IsSelectorChecked: true,
            IsEnabled: false,
            IsSelectorCommandEnabled: true));

        Assert.False(viewModel.IsSelectorChecked);
        Assert.False(viewModel.IsEnabled);
        Assert.True(viewModel.IsSelectorEnabled);
        Assert.Equal(Avalonia.Media.Color.Parse("#D0D0D0"), BrushColor(viewModel.SelectorBackground));
        Assert.Empty(dispatcher.Requests);
    }

    [Fact]
    public void ApplyRuntime_cannot_reset_selector_commands_without_seed_bindings()
    {
        var dispatcher = new CapturingEquipmentCommandDispatcher();
        var card = RouteMapSeed.Create().MapEquipment.Single();
        var viewModel = new EquipmentCardViewModel(card, dispatcher);

        var runtime = new RouteObjectRuntimeState(
            card.Id,
            RouteObjectState.Disabled,
            card.StatusText,
            ValueText: null,
            IsVisible: true,
            CanStart: false,
            CanStop: false,
            IsSelectorChecked: true,
            IsEnabled: false,
            IsSelectorCommandEnabled: true,
            ShouldResetSelectorCommands: true);

        viewModel.ApplyRuntime(runtime);
        viewModel.ApplyRuntime(runtime);

        Assert.False(viewModel.IsSelectorChecked);
        Assert.True(viewModel.IsSelectorEnabled);
        Assert.Empty(dispatcher.Requests);
    }

    [Fact]
    public void Button_colors_resolve_pressed_checked_normal_priority()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(Avalonia.Media.Color.Parse("#D0D0D0"), BrushColor(viewModel.StartBackground));
        Assert.Equal(Avalonia.Media.Color.Parse("#101820"), BrushColor(viewModel.StartForeground));
        Assert.Equal(Avalonia.Media.Color.Parse("#FF2626"), BrushColor(viewModel.StopBackground));
        Assert.Equal(Avalonia.Media.Color.Parse("#101820"), BrushColor(viewModel.StopForeground));
        Assert.Equal(Avalonia.Media.Color.Parse("#D0D0D0"), BrushColor(viewModel.SelectorBackground));
        Assert.Equal(Avalonia.Media.Color.Parse("#101820"), BrushColor(viewModel.SelectorForeground));

        viewModel.IsStartHovered = true;
        Assert.Equal(Avalonia.Media.Color.Parse("#8AFF8E"), BrushColor(viewModel.StartBackground));
        viewModel.IsStartHovered = false;

        viewModel.IsSelectorChecked = true;

        Assert.Equal(Avalonia.Media.Color.Parse("#003CA3"), BrushColor(viewModel.SelectorBackground));
        Assert.Equal(Avalonia.Media.Color.Parse("#FFFFFF"), BrushColor(viewModel.SelectorForeground));

        viewModel.IsSelectorPressed = true;

        Assert.Equal(Avalonia.Media.Color.Parse("#8AB5FF"), BrushColor(viewModel.SelectorBackground));
        Assert.Equal(Avalonia.Media.Color.Parse("#101820"), BrushColor(viewModel.SelectorForeground));

        viewModel.IsSelectorPressed = false;

        viewModel.IsStartChecked = true;

        Assert.False(viewModel.IsStartChecked);
        Assert.Equal(Avalonia.Media.Color.Parse("#D0D0D0"), BrushColor(viewModel.StartBackground));
        Assert.Equal(Avalonia.Media.Color.Parse("#101820"), BrushColor(viewModel.StartForeground));
        Assert.Equal(Avalonia.Media.Color.Parse("#FF2626"), BrushColor(viewModel.StopBackground));

        viewModel.IsStartPressed = true;
        Assert.Equal(Avalonia.Media.Color.Parse("#00D107"), BrushColor(viewModel.StartBackground));
        viewModel.IsStartPressed = false;

        viewModel.ApplyRuntime(new RouteObjectRuntimeState(viewModel.Id, RouteObjectState.Idle, "Выключено",
            null, true, true, true, IsStopChecked: true));

        Assert.False(viewModel.IsStartChecked);
        Assert.True(viewModel.IsStopChecked);
        Assert.Equal(Avalonia.Media.Color.Parse("#D0D0D0"), BrushColor(viewModel.StartBackground));
        Assert.Equal(Avalonia.Media.Color.Parse("#D10000"), BrushColor(viewModel.StopBackground));
        Assert.Equal(Avalonia.Media.Color.Parse("#FFFFFF"), BrushColor(viewModel.StopForeground));

        viewModel.IsStopHovered = true;
        Assert.Equal(Avalonia.Media.Color.Parse("#D10000"), BrushColor(viewModel.StopBackground));

        viewModel.IsStartPressed = true;
        viewModel.IsStopPressed = true;

        Assert.Equal(Avalonia.Media.Color.Parse("#00D107"), BrushColor(viewModel.StartBackground));
        Assert.Equal(Avalonia.Media.Color.Parse("#101820"), BrushColor(viewModel.StartForeground));
        Assert.Equal(Avalonia.Media.Color.Parse("#D10000"), BrushColor(viewModel.StopBackground));
        Assert.Equal(Avalonia.Media.Color.Parse("#FFFFFF"), BrushColor(viewModel.StopForeground));

        viewModel.IsStopPressed = false;
        viewModel.ApplyRuntime(new RouteObjectRuntimeState(viewModel.Id, RouteObjectState.Idle, "Выключено",
            null, true, true, true, IsStopChecked: false));
        Assert.Equal(Avalonia.Media.Color.Parse("#FF8A8A"), BrushColor(viewModel.StopBackground));
    }

    [Theory]
    [InlineData("Ожидание", "warning")]
    [InlineData("Выключено", "muted")]
    [InlineData("Выключен", "muted")]
    [InlineData("Не в сети", "muted")]
    [InlineData("Авария", "fault")]
    [InlineData("Выполнение", "ready")]
    [InlineData("Выгрузка", "ready")]
    [InlineData("Загрузка", "ready")]
    public void StatusBrush_maps_known_status_text(string statusText, string expectedBrush)
    {
        var viewModel = CreateViewModel();

        viewModel.StatusText = statusText;

        Assert.Same(ExpectedBrush(expectedBrush), viewModel.StatusBrush);
    }

    [Fact]
    public void StatusBrush_uses_muted_fallback_for_unknown_status()
    {
        var viewModel = CreateViewModel();

        viewModel.StatusText = "Неизвестно";

        Assert.Same(RouteMapPalette.MutedTextBrush, viewModel.StatusBrush);
    }

    [Fact]
    public void ApplyRouteSelection_shows_selected_send_and_return_points()
    {
        var definition = RouteMapSeed.Create();
        var viewModel = CreateViewModel();
        var roleStates = CreateRoleStates(definition);

        viewModel.ApplyRouteSelection(CreateChainNodes(definition), roleStates);

        Assert.Equal("БЕТОНОУКЛ.", viewModel.SendPointTitle);
        Assert.Equal("БСУ 1", viewModel.ReturnPointTitle);
        Assert.Equal("Отправить: БЕТОНОУКЛ.", viewModel.SendPointText);
        Assert.Equal("Возврат: БСУ 1", viewModel.ReturnPointText);
    }

    [Fact]
    public void ApplyRouteSelection_updates_points_after_role_change()
    {
        var definition = RouteMapSeed.Create();
        var viewModel = CreateViewModel();
        var roleStates = RouteNodeRoleStateTransitions.ToggleTarget(
            CreateRoleStates(definition),
            "bsu_2");

        viewModel.ApplyRouteSelection(CreateChainNodes(definition), roleStates);

        Assert.Equal("БСУ 2", viewModel.SendPointTitle);
        Assert.Equal("БСУ 1", viewModel.ReturnPointTitle);
    }

    [Fact]
    public void ApplyRouteSelection_uses_dash_when_points_are_not_selected()
    {
        var definition = RouteMapSeed.Create();
        var viewModel = CreateViewModel();
        var roleStates = definition.Nodes.ToDictionary(
            x => x.Id,
            x => new RouteNodeRoleState(x.Id, IsLoader: false, IsTarget: false));

        viewModel.ApplyRouteSelection(CreateChainNodes(definition), roleStates);

        Assert.Equal(EquipmentCardViewModel.EmptyRoutePointText, viewModel.SendPointTitle);
        Assert.Equal(EquipmentCardViewModel.EmptyRoutePointText, viewModel.ReturnPointTitle);
        Assert.Equal("Отправить: —", viewModel.SendPointText);
        Assert.Equal("Возврат: —", viewModel.ReturnPointText);
    }

    private static EquipmentCardViewModel CreateViewModel()
    {
        return new EquipmentCardViewModel(
            RouteMapSeed.Create().MapEquipment.Single() with { StatusMapping = null },
            new CapturingEquipmentCommandDispatcher());
    }

    private static RouteObjectRuntimeState BucketRuntime(ushort code = 0, bool isStopChecked = false, bool isConnected = true) =>
        new RouteMapRuntimeMapper(RouteMapSeed.Create()).Map(new Dictionary<string, SignalValue>
        {
            ["equip.bucket.text"] = new("equip.bucket.text", code, SignalValueType.UInt16, DateTimeOffset.UnixEpoch, true, false),
            ["equip.bucket.stop"] = new("equip.bucket.stop", isStopChecked, SignalValueType.Bool, DateTimeOffset.UnixEpoch, true, false),
            [RouteMapSystemSignalIds.ConnectionConnected] = new(RouteMapSystemSignalIds.ConnectionConnected,
                isConnected, SignalValueType.Bool, DateTimeOffset.UnixEpoch, true, false)
        }).Find("equip.bucket")!;

    private static IReadOnlyList<RouteNode> CreateChainNodes(RouteMapDefinition definition)
    {
        var nodesById = definition.Nodes.ToDictionary(x => x.Id);
        return definition.Chains
            .Single()
            .NodeIds
            .Select(x => nodesById[x])
            .ToArray();
    }

    private static IReadOnlyDictionary<string, RouteNodeRoleState> CreateRoleStates(RouteMapDefinition definition)
    {
        return definition.Nodes.ToDictionary(
            x => x.Id,
            x => new RouteNodeRoleState(x.Id, x.IsLoader, x.IsTarget));
    }

    private static Avalonia.Media.IBrush ExpectedBrush(string key)
    {
        return key switch
        {
            "warning" => RouteMapPalette.WarningBrush,
            "muted" => RouteMapPalette.MutedTextBrush,
            "fault" => RouteMapPalette.FaultBrush,
            "ready" => RouteMapPalette.ReadyBrush,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
        };
    }

    private static Avalonia.Media.Color BrushColor(Avalonia.Media.IBrush brush) =>
        Assert.IsType<Avalonia.Media.SolidColorBrush>(brush).Color;

    private sealed class CapturingEquipmentCommandDispatcher : IEquipmentCommandDispatcher
    {
        public List<SignalWriteRequest> Requests { get; } = new();

        public Task DispatchAsync(SignalWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingEquipmentCommandDispatcher : IEquipmentCommandDispatcher
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<SignalWriteRequest> Requests { get; } = new();
        public Task Completed => _completion.Task;
        public Task DispatchAsync(SignalWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return _completion.Task;
        }
        public void Complete() => _completion.SetResult();
    }

    private sealed class FailingEquipmentCommandDispatcher : IEquipmentCommandDispatcher
    {
        public List<SignalWriteRequest> Requests { get; } = new();
        public Task DispatchAsync(SignalWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            throw new InvalidOperationException("transport failed");
        }
    }
}
