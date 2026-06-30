using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive;
using System.Reactive.Subjects;
using Configurator.Application.Services;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Models;
using ReactiveUI;

namespace Configurator.Desktop.Workspace.RouteMap.Settings;

public sealed class RouteMapSettingsViewModel : ReactiveObject, IDisposable
{
    private readonly RouteMapConfigurationManager _manager;
    private readonly RouteMapConfigurationStorage _storage;
    private readonly IRouteMapSettingsFilePicker _filePicker;
    private readonly IRouteMapSignalRuntime? _signalRuntime;
    private readonly IAppConfigService? _appConfigService;
    private readonly Subject<bool> _result = new();
    private readonly Dictionary<RouteMapConfigurationItem, string> _knownIds = new();
    private RouteMapConfigurationDocument _draft;
    private RouteChainConfiguration? _selectedChain;
    private RouteNodeConfiguration? _selectedNode;
    private RouteSegmentConfiguration? _selectedSegment;
    private EquipmentCardConfiguration? _selectedCard;
    private RoutePlaceholderRuleConfiguration? _selectedPlaceholderRule;
    private string? _errorMessage;
    private string? _statusMessage;
    private string _nodeSearch = string.Empty;
    private string _segmentSearch = string.Empty;
    private string _cardSearch = string.Empty;
    private string _placeholderSearch = string.Empty;
    private bool _useMockSimulation;

    public RouteMapSettingsViewModel(
        RouteMapConfigurationManager manager,
        RouteMapConfigurationStorage storage,
        IRouteMapSettingsFilePicker filePicker,
        IRouteMapSignalRuntime? signalRuntime = null,
        IAppConfigService? appConfigService = null)
    {
        _manager = manager;
        _storage = storage;
        _filePicker = filePicker;
        _signalRuntime = signalRuntime;
        _appConfigService = appConfigService;
        _draft = manager.CreateDraft();
        _useMockSimulation = signalRuntime?.CurrentSource != RouteMapSignalSource.Modbus;
        if (_signalRuntime is not null)
        {
            _signalRuntime.SourceChanged += OnSignalSourceChanged;
        }

        ApplyCommand = ReactiveCommand.Create(Apply);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        ReloadCommand = ReactiveCommand.CreateFromTask(ReloadAsync);
        ImportCommand = ReactiveCommand.CreateFromTask(ImportAsync);
        ExportCommand = ReactiveCommand.CreateFromTask(ExportAsync);
        CloseCommand = ReactiveCommand.Create(() => _result.OnNext(false));

        AddChainCommand = ReactiveCommand.Create(AddChain);
        DuplicateChainCommand = ReactiveCommand.Create(DuplicateChain);
        DeleteChainCommand = ReactiveCommand.Create(DeleteChain);
        AddNodeCommand = ReactiveCommand.Create(AddNode);
        DuplicateNodeCommand = ReactiveCommand.Create(DuplicateNode);
        DeleteNodeCommand = ReactiveCommand.Create(DeleteNode);
        AddSegmentCommand = ReactiveCommand.Create(AddSegment);
        DuplicateSegmentCommand = ReactiveCommand.Create(DuplicateSegment);
        DeleteSegmentCommand = ReactiveCommand.Create(DeleteSegment);
        AddCardCommand = ReactiveCommand.Create(AddCard);
        DuplicateCardCommand = ReactiveCommand.Create(DuplicateCard);
        DeleteCardCommand = ReactiveCommand.Create(DeleteCard);
        AddPlaceholderCommand = ReactiveCommand.Create(AddPlaceholder);
        DuplicatePlaceholderCommand = ReactiveCommand.Create(DuplicatePlaceholder);
        DeletePlaceholderCommand = ReactiveCommand.Create(DeletePlaceholder);
        AddBindingCommand = ReactiveCommand.Create<string>(AddBinding);
        RemoveBindingCommand = ReactiveCommand.Create<SignalBindingConfiguration>(RemoveBinding);
        AddCardParameterCommand = ReactiveCommand.Create(AddCardParameter);
        RemoveCardParameterCommand = ReactiveCommand.Create<EquipmentCardParameterConfiguration>(RemoveCardParameter);

        AttachDraft(_draft);
        SelectFirstItems();
        ErrorMessage = manager.LastLoadError;
    }

    public IObservable<bool> Result => _result;
    public RouteMapConfigurationDocument Draft => _draft;
    public string ActiveFilePath => _manager.ActiveFilePath;
    public IReadOnlyList<RouteNodeKind> NodeKinds { get; } = Enum.GetValues<RouteNodeKind>();
    public IReadOnlyList<RouteNodeMenuKind> NodeMenuKinds { get; } = Enum.GetValues<RouteNodeMenuKind>();
    public IReadOnlyList<RouteNodeLabelPlacement> NodeLabelPlacements { get; } = Enum.GetValues<RouteNodeLabelPlacement>();
    public IReadOnlyList<RouteObjectState> ObjectStates { get; } = Enum.GetValues<RouteObjectState>();
    public IReadOnlyList<RouteSegmentKind> SegmentKinds { get; } = Enum.GetValues<RouteSegmentKind>();
    public IReadOnlyList<RouteElbowOrder> ElbowOrders { get; } = Enum.GetValues<RouteElbowOrder>();
    public IReadOnlyList<RouteLineCap> LineCaps { get; } = Enum.GetValues<RouteLineCap>();
    public IReadOnlyList<RouteCardVerticalAnchorKind> AnchorKinds { get; } = Enum.GetValues<RouteCardVerticalAnchorKind>();
    public IReadOnlyList<RoutePlaceholderPlacement> PlaceholderPlacements { get; } = Enum.GetValues<RoutePlaceholderPlacement>();
    public IReadOnlyList<RoutePlaceholderHeightMode> PlaceholderHeightModes { get; } = Enum.GetValues<RoutePlaceholderHeightMode>();
    public IReadOnlyList<SignalBindingRole> SignalBindingRoles { get; } = Enum.GetValues<SignalBindingRole>()
        .Where(role => !IsDeprecatedSignalRole(role))
        .ToArray();
    public IReadOnlyList<SignalBindingRole> NodeBindingRoles { get; } =
    [
        SignalBindingRole.Visible, SignalBindingRole.Fault,
        SignalBindingRole.ActiveRoute,
        SignalBindingRole.TargetCommand,
        SignalBindingRole.LoaderCommand,
    ];
    public IReadOnlyList<SignalBindingRole> SegmentBindingRoles { get; } =
    [
        SignalBindingRole.Visible, SignalBindingRole.Fault, SignalBindingRole.ActiveRoute,
    ];
    public IReadOnlyList<SignalBindingRole> CardBindingRoles { get; } =
    [
        SignalBindingRole.Text, SignalBindingRole.Value, SignalBindingRole.Visible,
        SignalBindingRole.StartCommand,
        SignalBindingRole.StopCommand,
        SignalBindingRole.StartOffFeedback,
        SignalBindingRole.StopOffFeedback,
        SignalBindingRole.Fault,
    ];
    public IReadOnlyList<SignalBindingRole> AutomaticModeBindingRoles { get; } =
        [SignalBindingRole.AutomaticModeCommand];
    public IReadOnlyList<SignalBindingRole> ManualModeBindingRoles { get; } =
        [SignalBindingRole.ManualModeCommand];
    public IReadOnlyList<SignalBindingRole> EmergencyBindingRoles { get; } =
        [SignalBindingRole.EmergencyCommand];
    public IReadOnlyList<SignalBindingDirection> SignalBindingDirections { get; } = Enum.GetValues<SignalBindingDirection>();
    public IReadOnlyList<SignalValueType> SignalValueTypes { get; } = Enum.GetValues<SignalValueType>();
    public IReadOnlyList<SignalBindingRole> EquipmentParameterRoles { get; } = [SignalBindingRole.EquipmentParameter];

    private static bool IsDeprecatedSignalRole(SignalBindingRole role) => role is
        SignalBindingRole.State or
        SignalBindingRole.TargetOffFeedback or
        SignalBindingRole.LoaderOffFeedback or
        SignalBindingRole.AutomaticModeOffFeedback or
        SignalBindingRole.ManualModeOffFeedback or
        SignalBindingRole.EmergencyOffFeedback;

    public IEnumerable<RouteNodeConfiguration> FilteredNodes => Filter(Draft.Nodes, NodeSearch, x => x.Title);
    public IEnumerable<RouteSegmentConfiguration> FilteredSegments => Filter(Draft.Segments, SegmentSearch, x => x.Title);
    public IEnumerable<EquipmentCardConfiguration> FilteredCards => Filter(Draft.Cards, CardSearch, x => x.Title);
    public IEnumerable<RoutePlaceholderRuleConfiguration> FilteredPlaceholderRules => Filter(Draft.PlaceholderRules, PlaceholderSearch, x => x.CardId);

    public RouteChainConfiguration? SelectedChain
    {
        get => _selectedChain;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedChain, value);
            this.RaisePropertyChanged(nameof(SelectedChainNodeIds));
            this.RaisePropertyChanged(nameof(SelectedChainSegmentIds));
        }
    }

    public RouteNodeConfiguration? SelectedNode { get => _selectedNode; set => this.RaiseAndSetIfChanged(ref _selectedNode, value); }
    public RouteSegmentConfiguration? SelectedSegment { get => _selectedSegment; set => this.RaiseAndSetIfChanged(ref _selectedSegment, value); }
    public EquipmentCardConfiguration? SelectedCard { get => _selectedCard; set => this.RaiseAndSetIfChanged(ref _selectedCard, value); }
    public RoutePlaceholderRuleConfiguration? SelectedPlaceholderRule { get => _selectedPlaceholderRule; set => this.RaiseAndSetIfChanged(ref _selectedPlaceholderRule, value); }

    public string SelectedChainNodeIds
    {
        get => SelectedChain is null ? string.Empty : string.Join(", ", SelectedChain.NodeIds);
        set => ReplaceIds(SelectedChain?.NodeIds, value);
    }

    public string SelectedChainSegmentIds
    {
        get => SelectedChain is null ? string.Empty : string.Join(", ", SelectedChain.SegmentIds);
        set => ReplaceIds(SelectedChain?.SegmentIds, value);
    }

    public string NodeSearch { get => _nodeSearch; set { this.RaiseAndSetIfChanged(ref _nodeSearch, value); this.RaisePropertyChanged(nameof(FilteredNodes)); } }
    public string SegmentSearch { get => _segmentSearch; set { this.RaiseAndSetIfChanged(ref _segmentSearch, value); this.RaisePropertyChanged(nameof(FilteredSegments)); } }
    public string CardSearch { get => _cardSearch; set { this.RaiseAndSetIfChanged(ref _cardSearch, value); this.RaisePropertyChanged(nameof(FilteredCards)); } }
    public string PlaceholderSearch { get => _placeholderSearch; set { this.RaiseAndSetIfChanged(ref _placeholderSearch, value); this.RaisePropertyChanged(nameof(FilteredPlaceholderRules)); } }
    public string? ErrorMessage { get => _errorMessage; private set { this.RaiseAndSetIfChanged(ref _errorMessage, value); this.RaisePropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public string? StatusMessage { get => _statusMessage; private set => this.RaiseAndSetIfChanged(ref _statusMessage, value); }
    public bool UseMockSimulation
    {
        get => _useMockSimulation;
        set
        {
            this.RaiseAndSetIfChanged(ref _useMockSimulation, value);
            this.RaisePropertyChanged(nameof(SelectedSignalSourceText));
            this.RaisePropertyChanged(nameof(SignalSourceDescription));
        }
    }
    public string CurrentSignalSourceText => _signalRuntime?.CurrentSource.ToString() ?? "Mock";
    public string SelectedSignalSourceText => UseMockSimulation ? "Mock" : "Modbus";
    public string SignalSourceDescription => UseMockSimulation
        ? "RouteMap получает тестовые значения и команды от встроенной Mock-симуляции."
        : "RouteMap использует общий Modbus Demo runtime и отдельную карту Modbus.DataMap. Переключение не запускает соединение автоматически.";

    public ReactiveCommand<Unit, Unit> ApplyCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> ReloadCommand { get; }
    public ReactiveCommand<Unit, Unit> ImportCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public ReactiveCommand<Unit, Unit> AddChainCommand { get; }
    public ReactiveCommand<Unit, Unit> DuplicateChainCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteChainCommand { get; }
    public ReactiveCommand<Unit, Unit> AddNodeCommand { get; }
    public ReactiveCommand<Unit, Unit> DuplicateNodeCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteNodeCommand { get; }
    public ReactiveCommand<Unit, Unit> AddSegmentCommand { get; }
    public ReactiveCommand<Unit, Unit> DuplicateSegmentCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteSegmentCommand { get; }
    public ReactiveCommand<Unit, Unit> AddCardCommand { get; }
    public ReactiveCommand<Unit, Unit> DuplicateCardCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteCardCommand { get; }
    public ReactiveCommand<Unit, Unit> AddPlaceholderCommand { get; }
    public ReactiveCommand<Unit, Unit> DuplicatePlaceholderCommand { get; }
    public ReactiveCommand<Unit, Unit> DeletePlaceholderCommand { get; }
    public ReactiveCommand<string, Unit> AddBindingCommand { get; }
    public ReactiveCommand<SignalBindingConfiguration, Unit> RemoveBindingCommand { get; }
    public ReactiveCommand<Unit, Unit> AddCardParameterCommand { get; }
    public ReactiveCommand<EquipmentCardParameterConfiguration, Unit> RemoveCardParameterCommand { get; }

    public void Dispose()
    {
        if (_signalRuntime is not null)
        {
            _signalRuntime.SourceChanged -= OnSignalSourceChanged;
        }
        DetachDraft(_draft);
        _result.Dispose();
    }

    internal void Apply()
    {
        EnsureDraftRequiredBindings();
        var result = _manager.Apply(Draft);
        HandleResult(result, "Настройки применены без записи файла.");
        if (result.IsSuccess)
        {
            ApplySignalSource();
        }
    }

    internal async Task SaveAsync()
    {
        EnsureDraftRequiredBindings();
        var result = await _manager.SaveAndApplyAsync(Draft);
        HandleResult(result, $"Настройки сохранены: {ActiveFilePath}");
        if (!result.IsSuccess)
        {
            return;
        }

        try
        {
            if (_appConfigService is not null)
            {
                var options = _appConfigService.GetSection<RouteMapRuntimeOptions>(RouteMapRuntimeOptions.SectionName);
                options.SignalSource = SelectedSource();
                await _appConfigService.SaveSectionAsync(RouteMapRuntimeOptions.SectionName, options);
            }

            ApplySignalSource();
            StatusMessage = $"Настройки RouteMap и источник {SelectedSignalSourceText} сохранены.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Карта сохранена, но источник данных сохранить не удалось: {ex.Message}";
        }
    }

    private void ApplySignalSource()
    {
        _signalRuntime?.SwitchSource(SelectedSource());
        this.RaisePropertyChanged(nameof(CurrentSignalSourceText));
        StatusMessage = $"Источник RouteMap переключен на {SelectedSignalSourceText}.";
    }

    private RouteMapSignalSource SelectedSource() =>
        UseMockSimulation ? RouteMapSignalSource.Mock : RouteMapSignalSource.Modbus;

    private void OnSignalSourceChanged(object? sender, RouteMapSignalSourceChangedEventArgs eventArgs)
    {
        this.RaisePropertyChanged(nameof(CurrentSignalSourceText));
    }

    private async Task ReloadAsync()
    {
        try { ReplaceDraft(await _manager.LoadActiveDraftAsync()); StatusMessage = "Активный JSON загружен в черновик."; ErrorMessage = null; }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    private async Task ImportAsync()
    {
        var path = await _filePicker.PickImportPathAsync();
        if (string.IsNullOrWhiteSpace(path)) return;
        try { ReplaceDraft(await _manager.ImportDraftAsync(path)); StatusMessage = $"Импортирован черновик: {path}"; ErrorMessage = null; }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    private async Task ExportAsync()
    {
        var path = await _filePicker.PickExportPathAsync();
        if (string.IsNullOrWhiteSpace(path)) return;
        HandleResult(await _manager.ExportDraftAsync(path, Draft), $"Черновик экспортирован: {path}");
    }

    private void HandleResult(RouteMapConfigurationOperationResult result, string successMessage)
    {
        ClearValidation();
        if (result.IsSuccess)
        {
            ErrorMessage = null;
            StatusMessage = successMessage;
            return;
        }

        foreach (var error in result.Errors)
            MarkInvalid(error);
        ErrorMessage = result.ErrorMessage ?? string.Join(Environment.NewLine, result.Errors.Take(12).Select(FormatError));
        StatusMessage = null;
    }

    private void AddChain() { var item = new RouteChainConfiguration { Id = UniqueId("chain", Draft.Chains.Select(x => x.Id)) }; Draft.Chains.Add(item); SelectedChain = item; }
    private void AddNode() { var item = new RouteNodeConfiguration { Id = UniqueId("node", Draft.Nodes.Select(x => x.Id)), Title = "Новый узел", X = 100, Y = 100 }; EnsureNodeBindings(item); Draft.Nodes.Add(item); SelectedNode = item; }
    private void AddSegment() { var item = new RouteSegmentConfiguration { Id = UniqueId("segment", Draft.Segments.Select(x => x.Id)), FromNodeId = Draft.Nodes.FirstOrDefault()?.Id ?? string.Empty, ToNodeId = Draft.Nodes.Skip(1).FirstOrDefault()?.Id ?? string.Empty }; EnsureSegmentActiveBinding(item); RouteSegmentActiveFragmentSynchronizer.Ensure(Draft, item); Draft.Segments.Add(item); SelectedSegment = item; }
    private void AddCard() { var item = new EquipmentCardConfiguration { Id = UniqueId("card", Draft.Cards.Select(x => x.Id)), Title = "Новая карточка" }; EnsureCardBindings(item); Draft.Cards.Add(item); SelectedCard = item; }
    private void AddPlaceholder() { var item = new RoutePlaceholderRuleConfiguration { Id = UniqueId("placeholder", Draft.PlaceholderRules.Select(x => x.Id)), CardId = Draft.Cards.FirstOrDefault()?.Id ?? string.Empty }; Draft.PlaceholderRules.Add(item); SelectedPlaceholderRule = item; }

    private void DuplicateChain() { if (SelectedChain is null) return; var item = Clone(x => x.Chains, SelectedChain); item.Id = UniqueId(SelectedChain.Id, Draft.Chains.Select(x => x.Id)); Draft.Chains.Add(item); SelectedChain = item; }
    private void DuplicateNode() { if (SelectedNode is null) return; var item = Clone(x => x.Nodes, SelectedNode); item.Id = UniqueId(SelectedNode.Id, Draft.Nodes.Select(x => x.Id)); RewriteNodeBindingIds(item); EnsureNodeBindings(item); Draft.Nodes.Add(item); SelectedNode = item; }
    private void DuplicateSegment() { if (SelectedSegment is null) return; var item = Clone(x => x.Segments, SelectedSegment); item.Id = UniqueId(SelectedSegment.Id, Draft.Segments.Select(x => x.Id)); RewriteSegmentBindingIds(item); EnsureSegmentActiveBinding(item); RouteSegmentActiveFragmentSynchronizer.Ensure(Draft, item); Draft.Segments.Add(item); SelectedSegment = item; }
    private void DuplicateCard() { if (SelectedCard is null) return; var sourceId = SelectedCard.Id; var item = Clone(x => x.Cards, SelectedCard); item.Id = UniqueId(SelectedCard.Id, Draft.Cards.Select(x => x.Id)); item.AttachedChainId = null; RewriteCardBindingIds(item, sourceId); EnsureCardBindings(item); Draft.Cards.Add(item); SelectedCard = item; }
    private void DuplicatePlaceholder() { if (SelectedPlaceholderRule is null) return; var item = Clone(x => x.PlaceholderRules, SelectedPlaceholderRule); item.Id = UniqueId(SelectedPlaceholderRule.Id, Draft.PlaceholderRules.Select(x => x.Id)); Draft.PlaceholderRules.Add(item); SelectedPlaceholderRule = item; }

    private void DeleteChain() { if (SelectedChain is null) return; var deps = Draft.Cards.Where(x => x.AttachedChainId == SelectedChain.Id).Select(x => $"карточка {x.Id}"); if (BlockDelete(deps)) return; Draft.Chains.Remove(SelectedChain); SelectedChain = Draft.Chains.FirstOrDefault(); }
    private void DeleteNode() { if (SelectedNode is null) return; var id = SelectedNode.Id; var deps = Draft.Segments.Where(x => x.FromNodeId == id || x.ToNodeId == id).Select(x => $"линия {x.Id}").Concat(Draft.Chains.Where(x => x.NodeIds.Contains(id)).Select(x => $"цепочка {x.Id}")).Concat(Draft.Cards.Where(x => x.VerticalAnchorNodeId == id).Select(x => $"карточка {x.Id}")); if (BlockDelete(deps)) return; Draft.Nodes.Remove(SelectedNode); SelectedNode = Draft.Nodes.FirstOrDefault(); }
    private void DeleteSegment() { if (SelectedSegment is null) return; var id = SelectedSegment.Id; var deps = Draft.Chains.Where(x => x.SegmentIds.Contains(id)).Select(x => $"цепочка {x.Id}"); if (BlockDelete(deps)) return; Draft.Segments.Remove(SelectedSegment); SelectedSegment = Draft.Segments.FirstOrDefault(); }
    private void DeleteCard() { if (SelectedCard is null) return; var id = SelectedCard.Id; var deps = Draft.PlaceholderRules.Where(x => x.CardId == id).Select(x => $"заглушка {x.Id}"); if (BlockDelete(deps)) return; Draft.Cards.Remove(SelectedCard); SelectedCard = Draft.Cards.FirstOrDefault(); }
    private void DeletePlaceholder() { if (SelectedPlaceholderRule is null) return; Draft.PlaceholderRules.Remove(SelectedPlaceholderRule); SelectedPlaceholderRule = Draft.PlaceholderRules.FirstOrDefault(); }

    private bool BlockDelete(IEnumerable<string> dependencies)
    {
        var items = dependencies.Distinct().ToArray();
        if (items.Length == 0) return false;
        ErrorMessage = "Удаление заблокировано. Сначала удалите ссылки: " + string.Join(", ", items);
        return true;
    }

    private void AddBinding(string scope)
    {
        var bindings = scope switch
        {
            "node" => SelectedNode?.Bindings,
            "segment" => SelectedSegment?.Bindings,
            "card" => SelectedCard?.Bindings,
            _ => null,
        };
        bindings?.Add(new SignalBindingConfiguration { SignalId = "signal.new", Direction = SignalBindingDirection.Read, ValueType = SignalValueType.Bool });
    }

    private void RemoveBinding(SignalBindingConfiguration binding)
    {
        if (IsRequiredBinding(binding))
        {
            ErrorMessage = $"Binding {binding.Role} обязателен для текущего объекта и не может быть удален.";
            return;
        }
        SelectedNode?.Bindings.Remove(binding);
        SelectedSegment?.Bindings.Remove(binding);
        SelectedCard?.Bindings.Remove(binding);
    }

    private void AddCardParameter()
    {
        if (SelectedCard is null)
            return;

        SelectedCard.Parameters.Add(new EquipmentCardParameterConfiguration
        {
            Title = "Параметр",
            Role = SignalBindingRole.EquipmentParameter,
            SignalId = $"{SelectedCard.Id}.parameter",
            Direction = SignalBindingDirection.ReadWrite,
            ValueType = SignalValueType.UInt16,
        });
    }

    private void RemoveCardParameter(EquipmentCardParameterConfiguration parameter)
    {
        SelectedCard?.Parameters.Remove(parameter);
    }

    private bool IsRequiredBinding(SignalBindingConfiguration binding)
    {
        if (SelectedNode?.Bindings.Contains(binding) == true)
            return binding.Role == SignalBindingRole.ActiveRoute
                || binding.Role == SignalBindingRole.TargetCommand && SelectedNode.MenuKind is RouteNodeMenuKind.SendOnly or RouteNodeMenuKind.SendAndReturn
                || binding.Role == SignalBindingRole.LoaderCommand && SelectedNode.MenuKind == RouteNodeMenuKind.SendAndReturn;
        if (SelectedSegment?.Bindings.Contains(binding) == true)
            return binding.Role == SignalBindingRole.ActiveRoute;
        if (SelectedCard?.Bindings.Contains(binding) == true)
            return binding.Role == SignalBindingRole.StartCommand && SelectedCard.CanStart
                || binding.Role == SignalBindingRole.StopCommand && SelectedCard.CanStop;
        return false;
    }

    private static void EnsureNodeBindings(RouteNodeConfiguration node)
    {
        EnsureBinding(node.Bindings, SignalBindingRole.ActiveRoute, $"route.node.{node.Id}.active", SignalBindingDirection.Read);
        if (node.MenuKind is RouteNodeMenuKind.SendOnly or RouteNodeMenuKind.SendAndReturn)
        {
            EnsureBinding(node.Bindings, SignalBindingRole.TargetCommand, $"route.node.{node.Id}.target", SignalBindingDirection.ReadWrite);
        }
        if (node.MenuKind == RouteNodeMenuKind.SendAndReturn)
        {
            EnsureBinding(node.Bindings, SignalBindingRole.LoaderCommand, $"route.node.{node.Id}.loader", SignalBindingDirection.ReadWrite);
        }
        RemoveBindings(node.Bindings, SignalBindingRole.State, SignalBindingRole.TargetOffFeedback, SignalBindingRole.LoaderOffFeedback);
    }

    private static void EnsureCardBindings(EquipmentCardConfiguration card)
    {
        card.StartButtonKind = RouteCommandButtonKind.Toggle;
        card.StopButtonKind = RouteCommandButtonKind.Toggle;

        if (card.CanStart)
            EnsureBinding(card.Bindings, SignalBindingRole.StartCommand, $"{card.Id}.start", SignalBindingDirection.ReadWrite);

        if (card.CanStop)
            EnsureBinding(card.Bindings, SignalBindingRole.StopCommand, $"{card.Id}.stop", SignalBindingDirection.ReadWrite);

        NormalizeOptionalOffFeedback(card.Bindings, SignalBindingRole.StartOffFeedback);
        NormalizeOptionalOffFeedback(card.Bindings, SignalBindingRole.StopOffFeedback);
        card.StartOffFeedbackEnabled = card.Bindings.Any(x => x.Role == SignalBindingRole.StartOffFeedback);
        card.StopOffFeedbackEnabled = card.Bindings.Any(x => x.Role == SignalBindingRole.StopOffFeedback);
        foreach (var parameter in card.Parameters)
            parameter.Role = SignalBindingRole.EquipmentParameter;
        RemoveBindings(card.Bindings, SignalBindingRole.State);
    }

    private void EnsureDraftRequiredBindings()
    {
        EnsureTopBarBindings(Draft.TopBar);
        foreach (var node in Draft.Nodes)
            EnsureNodeBindings(node);
        foreach (var segment in Draft.Segments)
        {
            EnsureSegmentActiveBinding(segment);
            RouteSegmentActiveFragmentSynchronizer.Ensure(Draft, segment);
        }
        foreach (var card in Draft.Cards)
            EnsureCardBindings(card);
    }

    private static void EnsureTopBarBindings(RouteTopBarConfiguration topBar)
    {
        EnsureBinding(topBar.Automatic.Bindings, SignalBindingRole.AutomaticModeCommand, "system.mode.automatic", SignalBindingDirection.ReadWrite);
        RemoveBindings(topBar.Automatic.Bindings, SignalBindingRole.AutomaticModeOffFeedback);
        EnsureBinding(topBar.Manual.Bindings, SignalBindingRole.ManualModeCommand, "system.mode.manual", SignalBindingDirection.ReadWrite);
        RemoveBindings(topBar.Manual.Bindings, SignalBindingRole.ManualModeOffFeedback);
        EnsureBinding(topBar.Emergency.Bindings, SignalBindingRole.EmergencyCommand, "system.emergency", SignalBindingDirection.ReadWrite);
        topBar.Emergency.ButtonKind = RouteCommandButtonKind.Toggle;
        topBar.Emergency.OffFeedbackEnabled = false;
        RemoveBindings(topBar.Emergency.Bindings, SignalBindingRole.EmergencyOffFeedback);
    }

    private static void EnsureBinding(
        ICollection<SignalBindingConfiguration> bindings,
        SignalBindingRole role,
        string signalId,
        SignalBindingDirection direction)
    {
        if (bindings.Any(x => x.Role == role))
            return;
        bindings.Add(new SignalBindingConfiguration
        {
            Role = role,
            SignalId = signalId,
            Direction = direction,
            ValueType = SignalValueType.Bool,
        });
    }

    private static void NormalizeOptionalOffFeedback(
        ICollection<SignalBindingConfiguration> bindings,
        SignalBindingRole role)
    {
        foreach (var binding in bindings.Where(x => x.Role == role))
        {
            binding.Direction = SignalBindingDirection.Read;
            binding.ValueType = SignalValueType.Bool;
        }
    }

    private static void RemoveBindings(
        ICollection<SignalBindingConfiguration> bindings,
        params SignalBindingRole[] roles)
    {
        var roleSet = roles.ToHashSet();
        foreach (var binding in bindings.Where(x => roleSet.Contains(x.Role)).ToArray())
            bindings.Remove(binding);
    }

    private static void RewriteNodeBindingIds(RouteNodeConfiguration node)
    {
        foreach (var binding in node.Bindings)
        {
            binding.SignalId = binding.Role switch
            {
                SignalBindingRole.ActiveRoute => $"route.node.{node.Id}.active",
                SignalBindingRole.TargetCommand => $"route.node.{node.Id}.target",
                SignalBindingRole.LoaderCommand => $"route.node.{node.Id}.loader",
                _ => binding.SignalId,
            };
        }
    }

    private static void RewriteCardBindingIds(EquipmentCardConfiguration card, string? oldId = null)
    {
        foreach (var binding in card.Bindings)
        {
            binding.SignalId = binding.Role switch
            {
                SignalBindingRole.StartCommand => $"{card.Id}.start",
                SignalBindingRole.StopCommand => $"{card.Id}.stop",
                SignalBindingRole.StartOffFeedback => $"{card.Id}.start.off",
                SignalBindingRole.StopOffFeedback => $"{card.Id}.stop.off",
                _ => binding.SignalId,
            };
        }

        foreach (var parameter in card.Parameters)
        {
            parameter.Role = SignalBindingRole.EquipmentParameter;
            if (string.IsNullOrWhiteSpace(parameter.SignalId))
            {
                parameter.SignalId = $"{card.Id}.parameter";
            }
            else if (!string.IsNullOrWhiteSpace(oldId) &&
                     parameter.SignalId.StartsWith(oldId + ".", StringComparison.Ordinal))
            {
                parameter.SignalId = card.Id + parameter.SignalId[oldId.Length..];
            }
        }
    }

    private static void RewriteSegmentBindingIds(RouteSegmentConfiguration segment)
    {
        foreach (var binding in segment.Bindings)
        {
            if (binding.Role == SignalBindingRole.ActiveRoute)
                binding.SignalId = $"route.{segment.Id}.active";
        }

        foreach (var fragment in segment.ActiveFragments)
            fragment.Binding.SignalId = $"route.{segment.Id}.fragment_{fragment.Index}.active";
    }

    private static void EnsureSegmentActiveBinding(RouteSegmentConfiguration segment)
    {
        RemoveBindings(segment.Bindings, SignalBindingRole.State);
        if (segment.Bindings.Any(x => x.Role == SignalBindingRole.ActiveRoute))
            return;

        segment.Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.ActiveRoute,
            SignalId = $"route.{segment.Id}.active",
            Direction = SignalBindingDirection.Read,
            ValueType = SignalValueType.Bool,
        });
    }

    private void ReplaceDraft(RouteMapConfigurationDocument document)
    {
        DetachDraft(_draft);
        _draft = document;
        this.RaisePropertyChanged(nameof(Draft));
        AttachDraft(_draft);
        SelectFirstItems();
        RaiseFilteredCollections();
    }

    private void AttachDraft(RouteMapConfigurationDocument document)
    {
        document.Chains.CollectionChanged += OnCollectionChanged;
        document.Nodes.CollectionChanged += OnCollectionChanged;
        document.Segments.CollectionChanged += OnCollectionChanged;
        document.Cards.CollectionChanged += OnCollectionChanged;
        document.PlaceholderRules.CollectionChanged += OnCollectionChanged;
        document.TopBar.Emergency.PropertyChanged += OnTopBarEmergencyPropertyChanged;
        foreach (var item in Items(document)) AttachItem(item);
    }

    private void DetachDraft(RouteMapConfigurationDocument document)
    {
        document.Chains.CollectionChanged -= OnCollectionChanged;
        document.Nodes.CollectionChanged -= OnCollectionChanged;
        document.Segments.CollectionChanged -= OnCollectionChanged;
        document.Cards.CollectionChanged -= OnCollectionChanged;
        document.PlaceholderRules.CollectionChanged -= OnCollectionChanged;
        document.TopBar.Emergency.PropertyChanged -= OnTopBarEmergencyPropertyChanged;
        foreach (var item in Items(document)) item.PropertyChanged -= OnItemPropertyChanged;
        _knownIds.Clear();
    }

    private void OnTopBarEmergencyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RouteTopBarEmergencyButtonConfiguration.ButtonKind)
            or nameof(RouteTopBarEmergencyButtonConfiguration.OffFeedbackEnabled))
            EnsureTopBarBindings(Draft.TopBar);
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null) foreach (RouteMapConfigurationItem item in e.OldItems) { item.PropertyChanged -= OnItemPropertyChanged; _knownIds.Remove(item); }
        if (e.NewItems is not null) foreach (RouteMapConfigurationItem item in e.NewItems) AttachItem(item);
        RaiseFilteredCollections();
    }

    private void AttachItem(RouteMapConfigurationItem item)
    {
        _knownIds[item] = item.Id;
        item.PropertyChanged += OnItemPropertyChanged;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is RouteNodeConfiguration node && e.PropertyName == nameof(RouteNodeConfiguration.MenuKind))
            EnsureNodeBindings(node);
        if (sender is EquipmentCardConfiguration changedCard &&
            e.PropertyName is nameof(EquipmentCardConfiguration.CanStart)
                or nameof(EquipmentCardConfiguration.CanStop)
                or nameof(EquipmentCardConfiguration.StartButtonKind)
                or nameof(EquipmentCardConfiguration.StopButtonKind)
                or nameof(EquipmentCardConfiguration.StartOffFeedbackEnabled)
                or nameof(EquipmentCardConfiguration.StopOffFeedbackEnabled))
            EnsureCardBindings(changedCard);
        if (e.PropertyName != nameof(RouteMapConfigurationItem.Id) || sender is not RouteMapConfigurationItem item) return;
        var oldId = _knownIds[item];
        var newId = item.Id;
        _knownIds[item] = newId;
        if (oldId == newId || string.IsNullOrWhiteSpace(oldId)) return;

        switch (item)
        {
            case RouteNodeConfiguration:
                foreach (var segment in Draft.Segments) { if (segment.FromNodeId == oldId) segment.FromNodeId = newId; if (segment.ToNodeId == oldId) segment.ToNodeId = newId; }
                foreach (var chain in Draft.Chains) ReplaceId(chain.NodeIds, oldId, newId);
                foreach (var card in Draft.Cards.Where(x => x.VerticalAnchorNodeId == oldId)) card.VerticalAnchorNodeId = newId;
                break;
            case RouteSegmentConfiguration:
                foreach (var chain in Draft.Chains) ReplaceId(chain.SegmentIds, oldId, newId);
                break;
            case RouteChainConfiguration:
                foreach (var card in Draft.Cards.Where(x => x.AttachedChainId == oldId)) card.AttachedChainId = newId;
                break;
            case EquipmentCardConfiguration:
                foreach (var rule in Draft.PlaceholderRules.Where(x => x.CardId == oldId)) rule.CardId = newId;
                break;
        }
        RaiseFilteredCollections();
    }

    private void ClearValidation()
    {
        foreach (var item in Items(Draft)) { item.IsInvalid = false; item.ValidationMessage = null; }
    }

    private void MarkInvalid(RouteMapConfigurationError error)
    {
        var item = Items(Draft).FirstOrDefault(x => x.Id == error.ObjectId);
        if (item is null) return;
        item.IsInvalid = true;
        item.ValidationMessage = string.IsNullOrWhiteSpace(item.ValidationMessage) ? error.Message : item.ValidationMessage + Environment.NewLine + error.Message;
    }

    private T Clone<T>(Func<RouteMapConfigurationDocument, ObservableCollection<T>> selector, T source) where T : RouteMapConfigurationItem
    {
        var clone = _storage.Clone(Draft);
        return selector(clone).First(x => x.Id == source.Id);
    }

    private static IEnumerable<RouteMapConfigurationItem> Items(RouteMapConfigurationDocument document)
    {
        foreach (var item in document.Chains) yield return item;
        foreach (var item in document.Nodes) yield return item;
        foreach (var item in document.Segments) yield return item;
        foreach (var item in document.Cards) yield return item;
        foreach (var item in document.PlaceholderRules) yield return item;
    }

    private void SelectFirstItems()
    {
        SelectedChain = Draft.Chains.FirstOrDefault();
        SelectedNode = Draft.Nodes.FirstOrDefault();
        SelectedSegment = Draft.Segments.FirstOrDefault();
        SelectedCard = Draft.Cards.FirstOrDefault();
        SelectedPlaceholderRule = Draft.PlaceholderRules.FirstOrDefault();
    }

    private void RaiseFilteredCollections()
    {
        this.RaisePropertyChanged(nameof(FilteredNodes));
        this.RaisePropertyChanged(nameof(FilteredSegments));
        this.RaisePropertyChanged(nameof(FilteredCards));
        this.RaisePropertyChanged(nameof(FilteredPlaceholderRules));
    }

    private static IEnumerable<T> Filter<T>(IEnumerable<T> source, string search, Func<T, string?> extra) where T : RouteMapConfigurationItem =>
        string.IsNullOrWhiteSpace(search) ? source : source.Where(x => x.Id.Contains(search, StringComparison.OrdinalIgnoreCase) || (extra(x)?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));

    private static void ReplaceIds(ObservableCollection<string>? target, string value)
    {
        if (target is null) return;
        target.Clear();
        foreach (var id in value.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) target.Add(id);
    }

    private static void ReplaceId(ObservableCollection<string> target, string oldId, string newId)
    {
        for (var i = 0; i < target.Count; i++) if (target[i] == oldId) target[i] = newId;
    }

    private static string UniqueId(string prefix, IEnumerable<string> ids)
    {
        var set = ids.ToHashSet(StringComparer.Ordinal);
        var baseId = string.IsNullOrWhiteSpace(prefix) ? "item" : prefix + "_copy";
        if (!set.Contains(baseId)) return baseId;
        for (var i = 2; ; i++) if (!set.Contains($"{baseId}_{i}")) return $"{baseId}_{i}";
    }

    private static string FormatError(RouteMapConfigurationError error) =>
        $"{error.Scope}{(string.IsNullOrWhiteSpace(error.ObjectId) ? string.Empty : $" '{error.ObjectId}'")}: {error.Message}";

}
