using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;
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
    public void Start_toggle_dispatches_true_and_false()
    {
        var dispatcher = new CapturingEquipmentCommandDispatcher();
        var card = RouteMapSeed.Create().MapEquipment.Single();
        var viewModel = new EquipmentCardViewModel(card, dispatcher);

        viewModel.IsStartChecked = true;
        viewModel.IsStartChecked = false;

        Assert.Collection(dispatcher.Requests,
            request => Assert.Equal(("equip.bucket.stop", false, SignalValueType.Bool), (request.SignalId, request.Value, request.ValueType)),
            request => Assert.Equal(("equip.bucket.start", true, SignalValueType.Bool), (request.SignalId, request.Value, request.ValueType)),
            request => Assert.Equal(("equip.bucket.start", false, SignalValueType.Bool), (request.SignalId, request.Value, request.ValueType)));
        Assert.False(viewModel.IsStartChecked);
    }

    [Fact]
    public void Stop_toggle_dispatches_true_and_false()
    {
        var dispatcher = new CapturingEquipmentCommandDispatcher();
        var card = RouteMapSeed.Create().MapEquipment.Single();
        var viewModel = new EquipmentCardViewModel(card, dispatcher);

        viewModel.IsStopChecked = true;
        viewModel.IsStopChecked = false;

        Assert.Collection(dispatcher.Requests,
            request => Assert.Equal(("equip.bucket.start", false, SignalValueType.Bool), (request.SignalId, request.Value, request.ValueType)),
            request => Assert.Equal(("equip.bucket.stop", true, SignalValueType.Bool), (request.SignalId, request.Value, request.ValueType)),
            request => Assert.Equal(("equip.bucket.stop", false, SignalValueType.Bool), (request.SignalId, request.Value, request.ValueType)));
        Assert.False(viewModel.IsStopChecked);
    }

    [Fact]
    public void Selector_toggle_dispatches_mutually_exclusive_values()
    {
        var dispatcher = new CapturingEquipmentCommandDispatcher();
        var card = RouteMapSeed.Create().MapEquipment.Single();
        var viewModel = new EquipmentCardViewModel(card, dispatcher);

        viewModel.IsSelectorChecked = true;
        viewModel.IsSelectorChecked = false;

        Assert.Collection(dispatcher.Requests,
            request => Assert.Equal(("equip.bucket.selector.off", false), (request.SignalId, request.Value)),
            request => Assert.Equal(("equip.bucket.selector.on", true), (request.SignalId, request.Value)),
            request => Assert.Equal(("equip.bucket.selector.on", false), (request.SignalId, request.Value)),
            request => Assert.Equal(("equip.bucket.selector.off", true), (request.SignalId, request.Value)));
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

        Assert.True(viewModel.IsSelectorChecked);
        Assert.False(viewModel.IsEnabled);
        Assert.True(viewModel.IsSelectorEnabled);
        Assert.Equal(Avalonia.Media.Color.Parse("#003CA3"), BrushColor(viewModel.SelectorBackground));
        Assert.Empty(dispatcher.Requests);
    }

    [Fact]
    public void ApplyRuntime_resets_selector_commands_once_when_enabled_false_signal_arrives()
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
        Assert.Collection(dispatcher.Requests,
            request => Assert.Equal(("equip.bucket.selector.on", false), (request.SignalId, request.Value)),
            request => Assert.Equal(("equip.bucket.selector.off", false), (request.SignalId, request.Value)));
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

        Assert.Equal(Avalonia.Media.Color.Parse("#00D107"), BrushColor(viewModel.StartBackground));
        Assert.Equal(Avalonia.Media.Color.Parse("#FFFFFF"), BrushColor(viewModel.StartForeground));
        Assert.Equal(Avalonia.Media.Color.Parse("#FF2626"), BrushColor(viewModel.StopBackground));

        viewModel.IsStartPressed = true;
        Assert.Equal(Avalonia.Media.Color.Parse("#00D107"), BrushColor(viewModel.StartBackground));
        viewModel.IsStartPressed = false;

        viewModel.IsStopChecked = true;

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
        viewModel.IsStopChecked = false;
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
}
