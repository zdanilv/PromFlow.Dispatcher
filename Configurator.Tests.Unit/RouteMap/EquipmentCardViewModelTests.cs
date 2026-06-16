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
            "Ожидание",
            ValueText: null,
            IsVisible: true,
            CanStart: true,
            CanStop: true,
            IsStartChecked: true,
            IsStopChecked: true));

        Assert.True(viewModel.IsStartChecked);
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

        Assert.Collection(
            dispatcher.Requests,
            request =>
            {
                Assert.Equal("equip.bucket.start", request.SignalId);
                Assert.Equal(true, request.Value);
                Assert.Equal(SignalValueType.Bool, request.ValueType);
            },
            request =>
            {
                Assert.Equal("equip.bucket.start", request.SignalId);
                Assert.Equal(false, request.Value);
                Assert.Equal(SignalValueType.Bool, request.ValueType);
            });
    }

    [Fact]
    public void Stop_toggle_dispatches_true_and_false()
    {
        var dispatcher = new CapturingEquipmentCommandDispatcher();
        var card = RouteMapSeed.Create().MapEquipment.Single();
        var viewModel = new EquipmentCardViewModel(card, dispatcher);

        viewModel.IsStopChecked = true;
        viewModel.IsStopChecked = false;

        Assert.Collection(
            dispatcher.Requests,
            request =>
            {
                Assert.Equal("equip.bucket.stop", request.SignalId);
                Assert.Equal(true, request.Value);
                Assert.Equal(SignalValueType.Bool, request.ValueType);
            },
            request =>
            {
                Assert.Equal("equip.bucket.stop", request.SignalId);
                Assert.Equal(false, request.Value);
                Assert.Equal(SignalValueType.Bool, request.ValueType);
            });
    }

    [Theory]
    [InlineData("Ожидание", "warning")]
    [InlineData("Выключен", "muted")]
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
