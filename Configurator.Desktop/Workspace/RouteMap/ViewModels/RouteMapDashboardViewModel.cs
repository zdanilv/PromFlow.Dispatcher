using Configurator.Application.Services;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Dialogs.HelpDialog;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Settings;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Avalonia.Threading;
using Microsoft.Extensions.Options;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive.Linq;

namespace Configurator.Desktop.Workspace.RouteMap.ViewModels;

public sealed class RouteMapDashboardViewModel : ViewModelBase, IDisposable
{
    private static readonly IReadOnlyDictionary<string, SignalValue> UnavailableSnapshot =
        new Dictionary<string, SignalValue>
        {
            [RouteMapSystemSignalIds.ConnectionConnected] = new(RouteMapSystemSignalIds.ConnectionConnected,
                false, SignalValueType.Bool, DateTimeOffset.MinValue, IsQualityGood: false, IsStale: true)
        };

    private readonly IDisposable _signalSubscription;
    private readonly IDisposable _definitionSubscription;
    private readonly IRouteMapRuntimeMapper<RouteMapRuntimeState> _runtimeMapper;
    private readonly IEquipmentCommandDispatcher _commandDispatcher;
    private readonly RouteMapModbusBindingDiagnostics? _bindingDiagnostics;
    private readonly RouteMapSessionJournal _sessionJournal;
    private readonly IOptionsMonitor<ModbusOptions> _modbusOptions;
    private readonly IEquipmentCardParametersDialogService? _cardParametersDialogService;
    private readonly IEquipmentParameterAutoDispatcher? _parameterAutoDispatcher;
    private readonly bool _isAdminMode;
    private RouteMapRuntimeState _runtimeState = RouteMapRuntimeState.Empty;
    private IReadOnlyDictionary<string, RouteNodeRoleState> _nodeRoleStates;
    private RouteMapDefinition _definition;
    private IReadOnlyDictionary<string, SignalValue>? _lastSignals;
    private string? _selectedObjectId;
    private string? _commandErrorMessage;
    private string _connectionStatusText = "Ожидание";
    private bool _wasConnectionAvailable;
    private int _disposed;
    private int _isDispatchingPendingParameters;

    public RouteMapDashboardViewModel(
        RouteMapConfigurationManager configurationManager,
        ISignalValueProvider signalProvider,
        IRouteMapRuntimeMapper<RouteMapRuntimeState> runtimeMapper,
        IEquipmentCommandDispatcher commandDispatcher,
        IRouteMapSettingsDialogService settingsDialogService,
        NotificationsPanelViewModel notificationsPanel,
        RouteMapSessionJournal sessionJournal,
        IOptionsMonitor<ModbusOptions> modbusOptions,
        IOptions<ApplicationOptions>? applicationOptions = null,
        RouteMapModbusBindingDiagnostics? bindingDiagnostics = null,
        IEquipmentCardParametersDialogService? cardParametersDialogService = null,
        IHelpDialogService? helpDialogService = null,
        IEquipmentParameterAutoDispatcher? parameterAutoDispatcher = null)
    {
        _runtimeMapper = runtimeMapper;
        _commandDispatcher = commandDispatcher;
        _bindingDiagnostics = bindingDiagnostics;
        _sessionJournal = sessionJournal;
        _modbusOptions = modbusOptions;
        _cardParametersDialogService = cardParametersDialogService;
        _parameterAutoDispatcher = parameterAutoDispatcher;
        _isAdminMode = applicationOptions?.Value.IsAdminMode ?? true;
        _definition = configurationManager.CurrentDefinition;
        IsConnectionStatusVisible = _isAdminMode;
        TopBar = new TopBarViewModel(
            settingsDialogService,
            commandDispatcher,
            Definition.TopBar,
            isSettingsVisible: _isAdminMode,
            palette: Definition.Display?.Palette,
            helpDialogService: helpDialogService,
            commandErrorHandler: message => CommandErrorMessage = message,
            commandSuccessHandler: () => CommandErrorMessage = null);
        MapEquipmentCards = new ObservableCollection<EquipmentCardViewModel>(
            Definition.MapEquipment.Select(CreateEquipmentCardViewModel));
        NotificationsPanel = notificationsPanel;
        _nodeRoleStates = CreateNodeRoleStates(Definition);
        ToggleNodeTargetCommand = ReactiveCommand.CreateFromTask<string>(ToggleNodeTargetAsync);
        ToggleNodeLoaderCommand = ReactiveCommand.CreateFromTask<string>(ToggleNodeLoaderAsync);
        ApplyEquipmentRouteSelections();

        _definitionSubscription = configurationManager.DefinitionChanges
            .Skip(1)
            .Subscribe(definition => Dispatcher.UIThread.Post(() =>
            {
                if (Volatile.Read(ref _disposed) == 0)
                    ApplyDefinition(definition);
            }));

        _signalSubscription = signalProvider is RouteMapSignalRuntime runtime
            ? runtime.ObserveUpdates().Subscribe(update => QueueSnapshot(update.Signals, runtime, update.Generation, update.SourceChanged))
            : signalProvider.Observe().Subscribe(signals => QueueSnapshot(signals));
    }

    private void QueueSnapshot(IReadOnlyDictionary<string, SignalValue>? signals,
        RouteMapSignalRuntime? runtime = null, long generation = 0, bool sourceChanged = false)
    {
        void ApplySnapshot()
        {
            if (Volatile.Read(ref _disposed) != 0 || (runtime is not null && !runtime.IsCurrentGeneration(generation)))
                return;

            _lastSignals = signals ?? UnavailableSnapshot;
            // Invalidation updates the UI; only live snapshots drive parameter dispatch on reconnect.
            ApplyRuntime(MapSnapshot(_lastSignals, Definition), isLiveSnapshot: signals is not null);
            if (signals is not null)
                _sessionJournal.RecordSignalSnapshot(signals, Definition, _modbusOptions.CurrentValue);
        }

        // Settings switch the source on the UI thread. Invalidate the old command gates before returning to input.
        if (sourceChanged && Dispatcher.UIThread.CheckAccess())
            ApplySnapshot();
        else
            Dispatcher.UIThread.Post(ApplySnapshot, sourceChanged ? DispatcherPriority.Send : DispatcherPriority.Default);
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
    public NotificationsPanelViewModel NotificationsPanel { get; }
    public bool IsConnectionStatusVisible { get; }
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

    public string? CommandErrorMessage
    {
        get => _commandErrorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _commandErrorMessage, value);
            this.RaisePropertyChanged(nameof(HasCommandError));
        }
    }

    public bool HasCommandError => !string.IsNullOrWhiteSpace(CommandErrorMessage);

    public string ConnectionStatusText
    {
        get => _connectionStatusText;
        private set => this.RaiseAndSetIfChanged(ref _connectionStatusText, value);
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
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _signalSubscription.Dispose();
        _definitionSubscription.Dispose();
        NotificationsPanel.Dispose();
        _bindingDiagnostics?.Dispose();
    }

    private void ApplyDefinition(RouteMapDefinition definition)
    {
        // A table edit must keep in-flight command and readback state on an otherwise unchanged card.
        var existingCards = _lastSignals is not null && Definition.Display?.Palette == definition.Display?.Palette
            ? MapEquipmentCards.ToDictionary(card => card.Id)
            : null;
        Definition = definition;
        MapEquipmentCards.Clear();
        foreach (var card in definition.MapEquipment)
        {
            var viewModel = existingCards?.GetValueOrDefault(card.Id);
            MapEquipmentCards.Add(viewModel is not null && viewModel.TryUpdateStatusMapping(card)
                ? viewModel
                : CreateEquipmentCardViewModel(card));
        }

        NodeRoleStates = CreateNodeRoleStates(definition);
        TopBar.ApplySettings(definition.TopBar, definition.Display?.Palette);

        if (!ContainsRuntimeObject(definition, SelectedObjectId))
            SelectedObjectId = null;

        ApplyEquipmentRouteSelections();
        if (_lastSignals is not null)
            ApplyRuntime(MapSnapshot(_lastSignals, definition), isLiveSnapshot: false);
    }

    private static IReadOnlyDictionary<string, RouteNodeRoleState> CreateNodeRoleStates(RouteMapDefinition definition) =>
        definition.Nodes
            .Where(node => node.Kind != RouteNodeKind.TerminalAnchor)
            .ToDictionary(node => node.Id, node => new RouteNodeRoleState(node.Id, node.IsLoader, node.IsTarget));

    private RouteMapRuntimeState MapSnapshot(IReadOnlyDictionary<string, SignalValue> signals, RouteMapDefinition definition) =>
        _runtimeMapper is RouteMapRuntimeMapper mapper ? mapper.Map(signals, definition) : _runtimeMapper.Map(signals);

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

    private void ApplyRuntime(RouteMapRuntimeState runtimeState, bool isLiveSnapshot)
    {
        var shouldDispatchPendingParameters =
            isLiveSnapshot && runtimeState.IsConnectionAvailable && !_wasConnectionAvailable;
        if (isLiveSnapshot)
            _wasConnectionAvailable = runtimeState.IsConnectionAvailable;
        RuntimeState = runtimeState;
        if (IsConnectionStatusVisible)
            ConnectionStatusText = runtimeState.ConnectionStatusText;
        TopBar.ApplyRuntime(
            runtimeState.IsAutomaticMode,
            runtimeState.IsManualMode,
            runtimeState.IsResetActive,
            runtimeState.HasEmergency,
            runtimeState.IsConnectionAvailable,
            runtimeState.IsAutomaticCommandEnabled,
            runtimeState.IsManualCommandEnabled,
            runtimeState.IsResetCommandEnabled,
            runtimeState.IsEmergencyCommandEnabled);
        foreach (var card in MapEquipmentCards)
            card.ApplyRuntime(runtimeState.Find(card.Id), runtimeState.IsConnectionAvailable, isLiveSnapshot);

        ApplyNodeRoleReadback(runtimeState);

        if (shouldDispatchPendingParameters &&
            _parameterAutoDispatcher is not null &&
            Interlocked.CompareExchange(ref _isDispatchingPendingParameters, 1, 0) == 0)
        {
            _ = DispatchPendingParametersAsync();
        }
    }

    private EquipmentCardViewModel CreateEquipmentCardViewModel(EquipmentCommandCard card) =>
        new(
            card,
            _commandDispatcher,
            Definition.Display?.Palette,
            _cardParametersDialogService,
            () => _lastSignals);

    private async Task DispatchPendingParametersAsync()
    {
        try
        {
            await _parameterAutoDispatcher!.DispatchPendingAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CommandErrorMessage = $"Не удалось автоматически отправить сохранённые параметры: {ex.Message}";
        }
        finally
        {
            Interlocked.Exchange(ref _isDispatchingPendingParameters, 0);
        }
    }

    private async Task ToggleNodeTargetAsync(string objectId)
    {
        await ToggleNodeRoleAsync(objectId, SignalBindingRole.TargetCommand, RouteNodeRoleStateTransitions.ToggleTarget);
    }

    private async Task ToggleNodeLoaderAsync(string objectId)
    {
        await ToggleNodeRoleAsync(objectId, SignalBindingRole.LoaderCommand, RouteNodeRoleStateTransitions.ToggleLoader);
    }

    private async Task ToggleNodeRoleAsync(
        string objectId,
        SignalBindingRole role,
        Func<IReadOnlyDictionary<string, RouteNodeRoleState>, string, IReadOnlyDictionary<string, RouteNodeRoleState>> transition)
    {
        var node = Definition.Nodes.FirstOrDefault(item => item.Id == objectId);
        if (!RuntimeState.IsConnectionAvailable ||
            RuntimeState.Find(objectId) is { IsEnabled: false } or { IsVisible: false } ||
            node is null || !node.IsVisible || node.Kind == RouteNodeKind.TerminalAnchor ||
            node.MenuKind == RouteNodeMenuKind.None ||
            (role == SignalBindingRole.LoaderCommand && node.MenuKind != RouteNodeMenuKind.SendAndReturn))
            return;

        var previous = NodeRoleStates;
        var next = transition(previous, objectId);
        var requests = RouteNodeRoleCommandWriter.CreateTransitionRequests(Definition, previous, next);
        // Check both the reset and the selection before sending either.
        if (_commandDispatcher is IRouteMapSignalRuntime { CurrentSource: RouteMapSignalSource.Modbus })
        {
            var points = _modbusOptions.CurrentValue.DataMap;
            foreach (var request in requests)
            {
                var point = points.FirstOrDefault(item => string.Equals(item.Name, request.SignalId, StringComparison.OrdinalIgnoreCase));
                if (point is null || !point.IsWritable || !SignalModbusTypeCompatibility.IsCompatible(request.ValueType, point.Type) ||
                    (request.RequiredWriteMode is { } mode && (!point.IsReadable || point.WriteMode != mode)))
                {
                    CommandErrorMessage = $"Команда узла не отправлена: для '{request.SignalId}' нет совместимой записи в Modbus.DataMap.";
                    return;
                }
            }

            if (_lastSignals?.ContainsKey(RouteMapSystemSignalIds.ConnectionConnected) != true)
                return;
        }

        NodeRoleStates = next;
        ApplyEquipmentRouteSelections();
        try
        {
            await RouteNodeRoleCommandWriter.DispatchTransitionAsync(requests, _commandDispatcher);
            CommandErrorMessage = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            NodeRoleStates = previous;
            ApplyEquipmentRouteSelections();
            CommandErrorMessage = $"Команда узла не отправлена: {ex.Message}";
        }
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
        CommandErrorMessage = null;
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
