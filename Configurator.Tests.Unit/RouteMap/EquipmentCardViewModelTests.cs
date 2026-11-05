using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;
using System.Reactive.Threading.Tasks;
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class EquipmentCardViewModelTests
{
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

        viewModel.IsStopChecked = true;
        Assert.False(viewModel.IsStopChecked);
        Assert.True(viewModel.IsStopPending);
        viewModel.ApplyRuntime(new RouteObjectRuntimeState(card.Id, RouteObjectState.Idle, card.StatusText,
            null, true, true, true, IsStopChecked: true));
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

        viewModel.IsStopChecked = true;
        Assert.True(viewModel.IsStopPending);
        viewModel.ApplyRuntime(new RouteObjectRuntimeState(card.Id, RouteObjectState.Idle, card.StatusText,
            null, true, true, true, IsStopChecked: true));
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

        viewModel.IsStopChecked = true;
        viewModel.ApplyRuntime(new RouteObjectRuntimeState(card.Id, RouteObjectState.Idle, card.StatusText,
            null, true, true, true, IsStopChecked: false));

        Assert.True(SpinWait.SpinUntil(() => !viewModel.IsStopPending, TimeSpan.FromSeconds(2)));
        Assert.False(viewModel.IsStopChecked);
        Assert.Contains("СТОП не подтверждён", viewModel.CommandErrorMessage);
        Assert.Single(dispatcher.Requests);
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
            RouteMapSeed.Create().MapEquipment.Single(),
            new CapturingEquipmentCommandDispatcher());
    }

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
