using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Settings;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Avalonia.Threading;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive.Linq;

namespace Configurator.Desktop.Workspace.RouteMap.ViewModels;

public sealed class RouteMapDashboardViewModel : ViewModelBase, IDisposable
{
    private readonly IDisposable _signalSubscription;
    private readonly IDisposable _definitionSubscription;
    private readonly IRouteMapRuntimeMapper<RouteMapRuntimeState> _runtimeMapper;
    private readonly IEquipmentCommandDispatcher _commandDispatcher;
    private readonly RouteMapModbusBindingDiagnostics? _bindingDiagnostics;
    private RouteMapRuntimeState _runtimeState = RouteMapRuntimeState.Empty;
    private IReadOnlyDictionary<string, RouteNodeRoleState> _nodeRoleStates;
    private RouteMapDefinition _definition;
    private IReadOnlyDictionary<string, SignalValue>? _lastSignals;
    private string? _selectedObjectId;

    public RouteMapDashboardViewModel(
        RouteMapConfigurationManager configurationManager,
        ISignalValueProvider signalProvider,
        IRouteMapRuntimeMapper<RouteMapRuntimeState> runtimeMapper,
        IEquipmentCommandDispatcher commandDispatcher,
        IRouteMapSettingsDialogService settingsDialogService,
        RouteMapModbusBindingDiagnostics? bindingDiagnostics = null)
    {
        _runtimeMapper = runtimeMapper;
        _commandDispatcher = commandDispatcher;
        _bindingDiagnostics = bindingDiagnostics;
        _definition = configurationManager.CurrentDefinition;
        TopBar = new TopBarViewModel(settingsDialogService, commandDispatcher, Definition.TopBar);
        MapEquipmentCards = new ObservableCollection<EquipmentCardViewModel>(
            Definition.MapEquipment.Select(x => new EquipmentCardViewModel(x, commandDispatcher, Definition.Display?.Palette)));
        RequestsPanel = new RequestsPanelViewModel(Definition.Requests, Definition.RequestTemplates);
        _nodeRoleStates = Definition.Nodes.ToDictionary(
            x => x.Id,
            x => new RouteNodeRoleState(x.Id, x.IsLoader, x.IsTarget));
        ToggleNodeTargetCommand = ReactiveCommand.CreateFromTask<string>(ToggleNodeTargetAsync);
        ToggleNodeLoaderCommand = ReactiveCommand.CreateFromTask<string>(ToggleNodeLoaderAsync);
        ApplyEquipmentRouteSelections();

        _definitionSubscription = configurationManager.DefinitionChanges
            .Skip(1)
            .Subscribe(definition => Dispatcher.UIThread.Post(() => ApplyDefinition(definition)));

        _signalSubscription = signalProvider
            .Observe()
            .Subscribe(signals =>
            {
                _lastSignals = signals;
                var runtimeState = runtimeMapper.Map(signals);
                Dispatcher.UIThread.Post(() => ApplyRuntime(runtimeState));
            });
    }

    public RouteMapDefinition Definition
    {
        get => _definition;
        private set
        {
            this.RaiseAndSetIfChanged(ref _definition, value);
            this.RaisePropertyChanged(nameof(SelectedObjectTitle));
        }
    }
    public TopBarViewModel TopBar { get; }
    public ObservableCollection<EquipmentCardViewModel> MapEquipmentCards { get; }
    public RequestsPanelViewModel RequestsPanel { get; }
    public ReactiveCommand<string, System.Reactive.Unit> ToggleNodeTargetCommand { get; }
    public ReactiveCommand<string, System.Reactive.Unit> ToggleNodeLoaderCommand { get; }

    public RouteMapRuntimeState RuntimeState
    {
        get => _runtimeState;
        private set => this.RaiseAndSetIfChanged(ref _runtimeState, value);
    }

    public IReadOnlyDictionary<string, RouteNodeRoleState> NodeRoleStates
    {
        get => _nodeRoleStates;
        private set => this.RaiseAndSetIfChanged(ref _nodeRoleStates, value);
    }

    public string? SelectedObjectId
    {
        get => _selectedObjectId;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedObjectId, value);
            this.RaisePropertyChanged(nameof(SelectedObjectTitle));
        }
    }

    public string SelectedObjectTitle
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SelectedObjectId))
                return "Объект не выбран";

            var title = Definition.Nodes.FirstOrDefault(x => x.Id == SelectedObjectId)?.Title
                ?? Definition.Vehicles.FirstOrDefault(x => x.Id == SelectedObjectId)?.Title
                ?? Definition.MapEquipment.FirstOrDefault(x => x.Id == SelectedObjectId)?.Title
                ?? Definition.Chains.FirstOrDefault(x => x.Id == SelectedObjectId)?.Id
                ?? Definition.Segments.FirstOrDefault(x => x.Id == SelectedObjectId)?.Id;

            return string.IsNullOrWhiteSpace(title) ? SelectedObjectId : title;
        }
    }

    public void Dispose()
    {
        _signalSubscription.Dispose();
        _definitionSubscription.Dispose();
        _bindingDiagnostics?.Dispose();
    }

    private void ApplyDefinition(RouteMapDefinition definition)
    {
        Definition = definition;
        MapEquipmentCards.Clear();
        foreach (var card in definition.MapEquipment)
            MapEquipmentCards.Add(new EquipmentCardViewModel(card, _commandDispatcher, definition.Display?.Palette));

        NodeRoleStates = definition.Nodes.ToDictionary(
            x => x.Id,
            x => new RouteNodeRoleState(x.Id, x.IsLoader, x.IsTarget));
        TopBar.ApplySettings(definition.TopBar);

        if (!ContainsRuntimeObject(definition, SelectedObjectId))
            SelectedObjectId = null;

        ApplyEquipmentRouteSelections();
        if (_lastSignals is not null)
            ApplyRuntime(_runtimeMapper.Map(_lastSignals));
    }

    private static bool ContainsRuntimeObject(RouteMapDefinition definition, string? objectId)
    {
        if (string.IsNullOrWhiteSpace(objectId))
            return true;

        return definition.Nodes.Any(x => x.Id == objectId)
            || definition.Segments.Any(x => x.Id == objectId)
            || definition.Vehicles.Any(x => x.Id == objectId)
            || definition.MapEquipment.Any(x => x.Id == objectId)
            || definition.Chains.Any(x => x.Id == objectId);
    }

    private void ApplyRuntime(RouteMapRuntimeState runtimeState)
    {
        RuntimeState = runtimeState;
        TopBar.ApplyRuntime(
            runtimeState.IsAutomaticMode,
            runtimeState.IsManualMode,
            runtimeState.HasEmergency,
            runtimeState.ConnectionStatusText);
        foreach (var card in MapEquipmentCards)
            card.ApplyRuntime(runtimeState.Find(card.Id));

        RequestsPanel.ApplyRuntime(runtimeState);
        ApplyNodeRoleReadback(runtimeState);
    }

    private async Task ToggleNodeTargetAsync(string objectId)
    {
        var previous = NodeRoleStates;
        NodeRoleStates = RouteNodeRoleStateTransitions.ToggleTarget(previous, objectId);
        ApplyEquipmentRouteSelections();
        await RouteNodeRoleCommandWriter.DispatchTransitionAsync(Definition, previous, NodeRoleStates, _commandDispatcher);
    }

    private async Task ToggleNodeLoaderAsync(string objectId)
    {
        var previous = NodeRoleStates;
        NodeRoleStates = RouteNodeRoleStateTransitions.ToggleLoader(previous, objectId);
        ApplyEquipmentRouteSelections();
        await RouteNodeRoleCommandWriter.DispatchTransitionAsync(Definition, previous, NodeRoleStates, _commandDispatcher);
    }

    private void ApplyNodeRoleReadback(RouteMapRuntimeState runtimeState)
    {
        var changed = false;
        var states = NodeRoleStates.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        foreach (var node in Definition.Nodes)
        {
            if (!states.TryGetValue(node.Id, out var current) || runtimeState.Find(node.Id) is not { } runtime)
                continue;
            var updated = current with
            {
                IsLoader = runtime.IsLoader ?? current.IsLoader,
                IsTarget = runtime.IsTarget ?? current.IsTarget,
            };
            if (updated == current)
                continue;
            states[node.Id] = updated;
            changed = true;
        }

        if (!changed)
            return;
        NodeRoleStates = states;
        ApplyEquipmentRouteSelections();
    }

    private void ApplyEquipmentRouteSelections()
    {
        var nodesById = Definition.Nodes.ToDictionary(x => x.Id);

        foreach (var card in MapEquipmentCards)
        {
            var chain = Definition.Chains.FirstOrDefault(x => x.AttachedEquipmentCardId == card.Id);
            if (chain is null)
            {
                card.ApplyRouteSelection(Array.Empty<RouteNode>(), NodeRoleStates);
                continue;
            }

            var chainNodes = chain.NodeIds
                .Where(nodesById.ContainsKey)
                .Select(x => nodesById[x])
                .ToArray();

            card.ApplyRouteSelection(chainNodes, NodeRoleStates);
        }
    }
}
