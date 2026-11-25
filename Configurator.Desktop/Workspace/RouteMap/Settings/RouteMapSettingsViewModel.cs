using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
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
    private readonly SerialDisposable _statusMappingSubscriptions = new();
    private readonly Dictionary<RouteMapConfigurationItem, string> _knownIds = new();
    private readonly Dictionary<RouteSegmentConfiguration, RouteNodeConfiguration> _replacedEndpoints = new();
    private readonly HashSet<RouteSegmentConfiguration> _segmentsWithChangedKind = [];
    private RouteMapConfigurationDocument _draft;
    private RouteMapConfigurationDocument _draftSource;
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
        _draft = manager.CreateDraft(out _draftSource);
        _useMockSimulation = signalRuntime?.CurrentSource != RouteMapSignalSource.Modbus;
        if (_signalRuntime is not null)
        {
            _signalRuntime.SourceChanged += OnSignalSourceChanged;
        }

        ApplyCommand = ReactiveCommand.Create(Apply);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        ReloadCommand = ReactiveCommand.CreateFromTask(ReloadAsync);
        RestoreDefaultsCommand = ReactiveCommand.Create(RestoreDefaults);
        ImportCommand = ReactiveCommand.CreateFromTask(ImportAsync);
        ExportCommand = ReactiveCommand.CreateFromTask(ExportAsync);
        CloseCommand = ReactiveCommand.Create(() => _result.OnNext(false));

        AddChainCommand = ReactiveCommand.Create(AddChain);
        DuplicateChainCommand = ReactiveCommand.Create(DuplicateChain);
        DeleteChainCommand = ReactiveCommand.Create(DeleteChain);
        AddNodeCommand = ReactiveCommand.Create(AddNode);
        DuplicateNodeCommand = ReactiveCommand.Create(DuplicateNode);
        DeleteNodeCommand = ReactiveCommand.Create(DeleteNode);
        AddSegmentCommand = ReactiveCommand.Create(() => AddSegment(RouteSegmentKind.Straight));
        AddDeadEndCommand = ReactiveCommand.Create(() => AddSegment(RouteSegmentKind.DeadEnd));
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
        AddFrequencyBindingCommand = ReactiveCommand.Create(AddFrequencyBinding);
        RemoveFrequencyBindingCommand = ReactiveCommand.Create(() => { FrequencyBinding = null; });
        AddStatusCommand = ReactiveCommand.Create(AddStatus);
        RemoveStatusCommand = ReactiveCommand.Create<EquipmentStatusEntryConfiguration>(RemoveStatus);
        AddCardParameterCommand = ReactiveCommand.Create(AddCardParameter);
        RemoveCardParameterCommand = ReactiveCommand.Create<EquipmentCardParameterConfiguration>(RemoveCardParameter);

        AttachDraft(_draft);
        SelectFirstItems();
        ErrorMessage = manager.LastLoadError;
    }

    public IObservable<bool> Result => _result;
    public RouteMapConfigurationDocument Draft => _draft;

    public SignalBindingConfiguration? FrequencyBinding
    {
        get => Draft.Map.FrequencyBinding;
        private set
        {
            Draft.Map.FrequencyBinding = value;
            this.RaisePropertyChanged();
        }
    }
    public string ActiveFilePath => _manager.ActiveFilePath;
    public IReadOnlyList<RouteNodeKind> NodeKinds { get; } = Enum.GetValues<RouteNodeKind>()
        .Where(kind => kind != RouteNodeKind.TerminalAnchor).ToArray();
    public IReadOnlyList<RouteNodeMenuKind> NodeMenuKinds { get; } = Enum.GetValues<RouteNodeMenuKind>();
    public IReadOnlyList<RouteNodeLabelPlacement> NodeLabelPlacements { get; } = Enum.GetValues<RouteNodeLabelPlacement>();
    public IReadOnlyList<RouteObjectState> ObjectStates { get; } = Enum.GetValues<RouteObjectState>();
    public IReadOnlyList<EquipmentStatusCategory> EquipmentStatusCategories { get; } = Enum.GetValues<EquipmentStatusCategory>();
    public IReadOnlyList<RouteSegmentKind> SegmentKinds { get; } = Enum.GetValues<RouteSegmentKind>();
    public IReadOnlyList<RouteMapEditorChoice<RouteSegmentKind>> SegmentKindChoices { get; } =
    [
        new(RouteSegmentKind.Straight, "Прямая"),
        new(RouteSegmentKind.RoundedElbow90, "Поворот 90°"),
        new(RouteSegmentKind.DeadEnd, "Тупик"),
    ];
    public IReadOnlyList<RouteMapEditorChoice<RouteTerminalSide>> TerminalSideChoices { get; } =
    [
        new(RouteTerminalSide.From, "У начала"),
        new(RouteTerminalSide.To, "У конца"),
    ];
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
        SignalBindingRole.Enabled,
        SignalBindingRole.TargetCommand,
        SignalBindingRole.LoaderCommand,
    ];
    private static readonly IReadOnlyList<SignalBindingRole> StandardSegmentBindingRoles =
    [
        SignalBindingRole.Visible, SignalBindingRole.Fault, SignalBindingRole.ActiveRoute,
        SignalBindingRole.Enabled,
    ];
    private static readonly IReadOnlyList<SignalBindingRole> DecorativeSegmentBindingRoles =
        [SignalBindingRole.Visible, SignalBindingRole.Fault, SignalBindingRole.Enabled];
    public IReadOnlyList<SignalBindingRole> SegmentBindingRoles =>
        CanEditSegmentActivity ? StandardSegmentBindingRoles : DecorativeSegmentBindingRoles;
    private static readonly IReadOnlyList<SignalBindingRole> PowerCardBindingRoles =
    [
        SignalBindingRole.Text, SignalBindingRole.Value, SignalBindingRole.Visible,
        SignalBindingRole.StartCommand,
        SignalBindingRole.StopCommand,
        SignalBindingRole.PowerFeedback,
        SignalBindingRole.StartOffFeedback,
        SignalBindingRole.StopOffFeedback,
        SignalBindingRole.Fault,
        SignalBindingRole.Enabled,
    ];
    private static readonly IReadOnlyList<SignalBindingRole> LegacyCardBindingRoles =
    [
        SignalBindingRole.Text, SignalBindingRole.Value, SignalBindingRole.Visible,
        SignalBindingRole.StartCommand, SignalBindingRole.StopCommand,
        SignalBindingRole.UncheckedCommand, SignalBindingRole.CheckedCommand,
        SignalBindingRole.StartOffFeedback, SignalBindingRole.StopOffFeedback,
        SignalBindingRole.Fault, SignalBindingRole.Enabled,
    ];
    public IReadOnlyList<SignalBindingRole> CardBindingRoles
    {
        get
        {
            var roles = SelectedCard is { } card && !UsesPowerFeedback(card) ? LegacyCardBindingRoles : PowerCardBindingRoles;
            return SelectedCard is { StartButtonKind: RouteCommandButtonKind.Momentary }
                ? [.. roles, SignalBindingRole.CommandResetFeedback]
                : roles;
        }
    }
    public bool CanAddStartResetFeedback =>
        SelectedCard is { CanStart: true, StartButtonKind: RouteCommandButtonKind.Momentary };
    public IReadOnlyList<SignalBindingRole> AutomaticModeBindingRoles { get; } =
        [];
    public IReadOnlyList<SignalBindingRole> ManualModeBindingRoles { get; } =
        [];
    public IReadOnlyList<SignalBindingRole> UnloadBindingRoles { get; } =
        [SignalBindingRole.UnloadCommand, SignalBindingRole.Enabled, SignalBindingRole.CommandResetFeedback];
    public IReadOnlyList<SignalBindingRole> RotationBindingRoles { get; } =
        [SignalBindingRole.RotationCommand, SignalBindingRole.Enabled, SignalBindingRole.CommandResetFeedback];
    public IReadOnlyList<SignalBindingRole> LoadBindingRoles { get; } =
        [SignalBindingRole.LoadCommand, SignalBindingRole.Enabled, SignalBindingRole.CommandResetFeedback];
    public IReadOnlyList<SignalBindingRole> ResetBindingRoles { get; } =
        [SignalBindingRole.ResetCommand, SignalBindingRole.Enabled];
    public IReadOnlyList<SignalBindingRole> EmergencyBindingRoles { get; } =
        [SignalBindingRole.EmergencyCommand, SignalBindingRole.Enabled];
    public IReadOnlyList<SignalBindingDirection> SignalBindingDirections { get; } = Enum.GetValues<SignalBindingDirection>();
    public IReadOnlyList<SignalValueType> SignalValueTypes { get; } = Enum.GetValues<SignalValueType>();
    public IReadOnlyList<SignalValueType> FrequencyValueTypes => RouteMapDisplaySettings.FrequencyValueTypes;
    public IReadOnlyList<SignalBindingRole> EquipmentParameterRoles { get; } = [SignalBindingRole.EquipmentParameter];
    public IReadOnlyList<RouteMapEditorChoice<EquipmentParameterVisibility>> EquipmentParameterVisibilityChoices { get; } =
    [
        new(EquipmentParameterVisibility.User, "Для пользователя"),
        new(EquipmentParameterVisibility.Admin, "Только для администратора"),
    ];

    private static bool IsDeprecatedSignalRole(SignalBindingRole role) => role is
        SignalBindingRole.State or
        SignalBindingRole.TargetOffFeedback or
        SignalBindingRole.LoaderOffFeedback or
        SignalBindingRole.AutomaticModeCommand or
        SignalBindingRole.ManualModeCommand or
        SignalBindingRole.AutomaticModeOffFeedback or
        SignalBindingRole.ManualModeOffFeedback or
        SignalBindingRole.EmergencyOffFeedback;

    private IEnumerable<RouteNodeConfiguration> OrdinaryNodes => Draft.Nodes.Where(x => x.Kind != RouteNodeKind.TerminalAnchor);
    public IEnumerable<RouteNodeConfiguration> FilteredNodes => Filter(OrdinaryNodes, NodeSearch, x => x.Title);
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

    public RouteNodeConfiguration? SelectedNode
    {
        get => _selectedNode;
        set => this.RaiseAndSetIfChanged(ref _selectedNode, value?.Kind == RouteNodeKind.TerminalAnchor ? null : value);
    }
    public RouteSegmentConfiguration? SelectedSegment
    {
        get => _selectedSegment;
        set
        {
            if (ReferenceEquals(_selectedSegment, value))
                return;

            this.RaiseAndSetIfChanged(ref _selectedSegment, value);
            RaiseSegmentEditorProperties();
        }
    }
    public RouteSegmentKind? SelectedSegmentKind
    {
        get => SelectedSegment?.Kind;
        set
        {
            if (SelectedSegment is { } segment && value is { } kind && segment.Kind != kind)
                ChangeSegmentKind(segment, kind);
        }
    }
    public RouteTerminalSide? SelectedTerminalSide
    {
        get => IsDeadEnd ? SelectedSegment?.TerminalSide : null;
        set
        {
            if (SelectedSegment is not { Kind: RouteSegmentKind.DeadEnd } segment ||
                value is not (RouteTerminalSide.From or RouteTerminalSide.To) || segment.TerminalSide == value)
                return;

            (segment.FromNodeId, segment.ToNodeId) = (segment.ToNodeId, segment.FromNodeId);
            segment.TerminalSide = value;
            RaiseSegmentEditorProperties();
        }
    }
    public bool IsDeadEnd => SelectedSegmentKind == RouteSegmentKind.DeadEnd;
    public bool IsElbow => SelectedSegmentKind == RouteSegmentKind.RoundedElbow90;
    public bool IsFromTerminalAnchor => SelectedTerminalSide == RouteTerminalSide.From;
    public bool IsToTerminalAnchor => SelectedTerminalSide == RouteTerminalSide.To;
    public bool CanEditSegmentActivity => !IsDeadEnd ||
        SelectedSegment?.Bindings.Any(binding => binding.Role == SignalBindingRole.ActiveRoute) == true;
    public RouteNodeConfiguration? SelectedTerminalAnchor =>
        SelectedSegment is { Kind: RouteSegmentKind.DeadEnd } segment ? FindTerminalAnchor(segment) : null;
    public EquipmentCardConfiguration? SelectedCard
    {
        get => _selectedCard;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedCard, value);
            this.RaisePropertyChanged(nameof(CardBindingRoles));
            this.RaisePropertyChanged(nameof(CanAddStartResetFeedback));
            ObserveSelectedStatusMapping();
        }
    }
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
        : "RouteMap использует общий Modbus TCP runtime и отдельную карту Modbus.DataMap. Переключение не запускает соединение автоматически.";

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
    public ReactiveCommand<Unit, Unit> AddDeadEndCommand { get; }
    public ReactiveCommand<Unit, Unit> RestoreDefaultsCommand { get; }
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
    public ReactiveCommand<Unit, Unit> AddFrequencyBindingCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveFrequencyBindingCommand { get; }
    public ReactiveCommand<Unit, Unit> AddStatusCommand { get; }
    public ReactiveCommand<EquipmentStatusEntryConfiguration, Unit> RemoveStatusCommand { get; }
    public ReactiveCommand<Unit, Unit> AddCardParameterCommand { get; }
    public ReactiveCommand<EquipmentCardParameterConfiguration, Unit> RemoveCardParameterCommand { get; }

    public void Dispose()
    {
        if (_signalRuntime is not null)
        {
            _signalRuntime.SourceChanged -= OnSignalSourceChanged;
        }
        DetachDraft(_draft);
        _statusMappingSubscriptions.Dispose();
        _result.Dispose();
    }

    internal void Apply()
    {
        if (!ValidateEditedFragmentCounts())
            return;
        EnsureDraftRequiredBindings();
        var result = _manager.Apply(Draft, _draftSource);
        HandleResult(result, "Настройки применены без записи файла.");
        if (result.IsSuccess)
        {
            _draftSource = result.AppliedDocument!;
            ApplySignalSource();
        }
    }

    internal async Task SaveAsync()
    {
        if (!ValidateEditedFragmentCounts())
            return;
        EnsureDraftRequiredBindings();
        var result = await _manager.SaveAndApplyAsync(Draft, _draftSource);
        HandleResult(result, $"Настройки сохранены: {ActiveFilePath}");
        if (!result.IsSuccess)
        {
            return;
        }

        _draftSource = result.AppliedDocument!;
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
        try
        {
            var (draft, source) = await _manager.ReloadEditorDraftAsync();
            _draftSource = source;
            ReplaceDraft(draft);
            StatusMessage = "Активный JSON загружен в черновик.";
            ErrorMessage = null;
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    private void RestoreDefaults()
    {
        ReplaceDraft(_manager.CreateDefaultDraft());
        ErrorMessage = null;
        StatusMessage = "Стандартная карта загружена в черновик.";
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
    private void AddSegment(RouteSegmentKind kind)
    {
        var isDeadEnd = kind == RouteSegmentKind.DeadEnd;
        var station = isDeadEnd ? SelectedNode ?? OrdinaryNodes.FirstOrDefault() : OrdinaryNodes.FirstOrDefault();
        if (isDeadEnd && station is null)
        {
            ErrorMessage = "Сначала добавьте узел, к которому будет подключён тупик.";
            return;
        }

        var item = new RouteSegmentConfiguration
        {
            Id = UniqueId("segment", RuntimeIds()),
            Kind = kind,
            FromNodeId = station?.Id ?? string.Empty,
            ToNodeId = OrdinaryNodes.Skip(1).FirstOrDefault()?.Id ?? string.Empty,
        };
        if (isDeadEnd && station is not null)
        {
            item.Title = "ТУПИК";
            item.TerminalSide = RouteTerminalSide.To;
            SetTerminalEndpoint(item, CreateTerminalAnchor(item.Id, station.X + RouteMapSeed.UpperDeadEndLength, station.Y).Id);
            SegmentSearch = string.Empty;
        }

        EnsureSegmentActiveBinding(item);
        RouteSegmentActiveFragmentSynchronizer.Ensure(Draft, item);
        Draft.Segments.Add(item);
        SelectedSegment = item;
        ErrorMessage = null;
    }
    private void AddCard() { var item = new EquipmentCardConfiguration { Id = UniqueId("card", Draft.Cards.Select(x => x.Id)), Title = "Новая карточка", StartButtonKind = RouteCommandButtonKind.Momentary }; EnsureCardBindings(item); Draft.Cards.Add(item); SelectedCard = item; }
    private void AddPlaceholder() { var item = new RoutePlaceholderRuleConfiguration { Id = UniqueId("placeholder", Draft.PlaceholderRules.Select(x => x.Id)), CardId = Draft.Cards.FirstOrDefault()?.Id ?? string.Empty }; Draft.PlaceholderRules.Add(item); SelectedPlaceholderRule = item; }

    private void DuplicateChain() { if (SelectedChain is null) return; var item = Clone(x => x.Chains, SelectedChain); item.Id = UniqueId(SelectedChain.Id, Draft.Chains.Select(x => x.Id)); Draft.Chains.Add(item); SelectedChain = item; }
    private void DuplicateNode() { if (SelectedNode is null) return; var item = Clone(x => x.Nodes, SelectedNode); item.Id = UniqueId(SelectedNode.Id, Draft.Nodes.Select(x => x.Id)); RewriteNodeBindingIds(item); EnsureNodeBindings(item); Draft.Nodes.Add(item); SelectedNode = item; }
    private void DuplicateSegment()
    {
        if (SelectedSegment is not { } source)
            return;

        var item = Clone(x => x.Segments, source);
        item.Id = UniqueId(source.Id, RuntimeIds());
        if (source.Kind == RouteSegmentKind.DeadEnd)
        {
            if (FindTerminalAnchor(source) is not { } anchor)
            {
                ErrorMessage = "Для дублирования тупика требуется его терминальный якорь.";
                return;
            }

            var newAnchor = CreateTerminalAnchor(item.Id, anchor.X, anchor.Y);
            SetTerminalEndpoint(item, newAnchor.Id);
            item.State = RouteObjectState.Idle;
            item.Bindings.Clear();
            item.ActiveFragments.Clear();
            SegmentSearch = string.Empty;
        }
        else
        {
            RewriteSegmentBindingIds(item);
            EnsureSegmentActiveBinding(item);
            RouteSegmentActiveFragmentSynchronizer.Ensure(Draft, item);
        }

        Draft.Segments.Add(item);
        SelectedSegment = item;
        ErrorMessage = null;
    }
    private void DuplicateCard()
    {
        if (SelectedCard is not { } source)
            return;

        var item = Clone(x => x.Cards, source);
        item.Id = UniqueId(source.Id, Draft.Cards.Select(x => x.Id));
        item.AttachedChainId = null;
        RewriteCardBindingIds(item, source.Id);
        EnsureCardBindings(item);
        foreach (var parameter in item.Parameters)
            parameter.ClearSavedSetpoint();

        if (source.StatusMapping?.Entries is { } sourceEntries && item.StatusMapping?.Entries is { } entries)
        {
            for (var index = 0; index < sourceEntries.Count; index++)
            {
                if (sourceEntries[index] is { } sourceEntry)
                    entries[index].CodeInput = sourceEntry.CodeInput;
            }
        }

        Draft.Cards.Add(item);
        SelectedCard = item;
    }
    private void DuplicatePlaceholder() { if (SelectedPlaceholderRule is null) return; var item = Clone(x => x.PlaceholderRules, SelectedPlaceholderRule); item.Id = UniqueId(SelectedPlaceholderRule.Id, Draft.PlaceholderRules.Select(x => x.Id)); Draft.PlaceholderRules.Add(item); SelectedPlaceholderRule = item; }

    private void DeleteChain() { if (SelectedChain is null) return; var deps = Draft.Cards.Where(x => x.AttachedChainId == SelectedChain.Id).Select(x => $"карточка {x.Id}"); if (BlockDelete(deps)) return; Draft.Chains.Remove(SelectedChain); SelectedChain = Draft.Chains.FirstOrDefault(); }
    private void DeleteNode()
    {
        if (SelectedNode is not { } node || BlockDelete(NodeDependencies(node.Id)))
            return;
        Draft.Nodes.Remove(node);
        SelectedNode = OrdinaryNodes.FirstOrDefault();
    }

    private void DeleteSegment()
    {
        if (SelectedSegment is not { } segment)
            return;

        var anchor = FindTerminalAnchor(segment);
        var dependencies = SegmentDependencies(segment.Id);
        if (anchor is not null)
            dependencies = dependencies.Concat(NodeDependencies(anchor.Id, segment));
        if (BlockDelete(dependencies))
            return;

        Draft.Segments.Remove(segment);
        if (anchor is not null)
            Draft.Nodes.Remove(anchor);
        SelectedSegment = Draft.Segments.FirstOrDefault();
        ErrorMessage = null;
    }
    private void DeleteCard() { if (SelectedCard is null) return; var id = SelectedCard.Id; var deps = Draft.PlaceholderRules.Where(x => x.CardId == id).Select(x => $"заглушка {x.Id}"); if (BlockDelete(deps)) return; Draft.Cards.Remove(SelectedCard); SelectedCard = Draft.Cards.FirstOrDefault(); }
    private void DeletePlaceholder() { if (SelectedPlaceholderRule is null) return; Draft.PlaceholderRules.Remove(SelectedPlaceholderRule); SelectedPlaceholderRule = Draft.PlaceholderRules.FirstOrDefault(); }

    private void ChangeSegmentKind(RouteSegmentConfiguration segment, RouteSegmentKind kind)
    {
        var becomesTerminal = kind == RouteSegmentKind.DeadEnd;
        if (becomesTerminal || segment.Kind == RouteSegmentKind.DeadEnd)
        {
            var anchor = FindTerminalAnchor(segment);
            var dependencies = SegmentDependencies(segment.Id);
            if (anchor is not null)
                dependencies = dependencies.Concat(NodeDependencies(anchor.Id, segment));
            if (dependencies.Distinct().ToArray() is { Length: > 0 } references)
            {
                ErrorMessage = "Смена типа линии заблокирована. Сначала удалите ссылки: " + string.Join(", ", references);
                this.RaisePropertyChanged(nameof(SelectedSegmentKind));
                return;
            }

            if (becomesTerminal)
            {
                segment.TerminalSide ??= RouteTerminalSide.To;
                var replacedNode = OrdinaryNodes.FirstOrDefault(node => node.Id == TerminalEndpointId(segment));
                var stationId = segment.TerminalSide == RouteTerminalSide.From ? segment.ToNodeId : segment.FromNodeId;
                var station = OrdinaryNodes.FirstOrDefault(node => node.Id == stationId);
                if (station is null)
                {
                    ErrorMessage = "Укажите обычный узел на противоположном конце линии.";
                    this.RaisePropertyChanged(nameof(SelectedSegmentKind));
                    return;
                }

                if (replacedNode is not null && replacedNode != station)
                    _replacedEndpoints[segment] = replacedNode;
                var position = replacedNode is not null && (replacedNode.X, replacedNode.Y) != (station.X, station.Y)
                    ? (replacedNode.X, replacedNode.Y)
                    : (station.X + RouteMapSeed.UpperDeadEndLength, station.Y);
                SetTerminalEndpoint(segment, CreateTerminalAnchor(segment.Id, position.Item1, position.Item2).Id);
            }
            else
            {
                _replacedEndpoints.Remove(segment, out var replacedNode);
                SetTerminalEndpoint(segment, replacedNode is not null && Draft.Nodes.Contains(replacedNode)
                    ? replacedNode.Id
                    : string.Empty);
                if (anchor is not null)
                    Draft.Nodes.Remove(anchor);
            }
        }

        segment.Kind = kind;
        _segmentsWithChangedKind.Add(segment);
        EnsureSegmentActiveBinding(segment);
        ErrorMessage = null;
        RaiseSegmentEditorProperties();
    }

    private RouteNodeConfiguration CreateTerminalAnchor(string segmentId, double x, double y)
    {
        var anchor = new RouteNodeConfiguration
        {
            Id = UniqueId(segmentId + "_anchor", RuntimeIds().Append(segmentId)),
            Kind = RouteNodeKind.TerminalAnchor,
            X = x,
            Y = y,
        };
        Draft.Nodes.Add(anchor);
        return anchor;
    }

    private RouteNodeConfiguration? FindTerminalAnchor(RouteSegmentConfiguration segment) =>
        segment.Kind == RouteSegmentKind.DeadEnd
            ? Draft.Nodes.FirstOrDefault(node => node.Kind == RouteNodeKind.TerminalAnchor && node.Id == TerminalEndpointId(segment))
            : null;

    private static string TerminalEndpointId(RouteSegmentConfiguration segment) =>
        segment.TerminalSide == RouteTerminalSide.From ? segment.FromNodeId : segment.ToNodeId;

    private static void SetTerminalEndpoint(RouteSegmentConfiguration segment, string id)
    {
        if (segment.TerminalSide == RouteTerminalSide.From)
            segment.FromNodeId = id;
        else
            segment.ToNodeId = id;
    }

    private IEnumerable<string> RuntimeIds() => Draft.Nodes.Select(x => x.Id)
        .Concat(Draft.Segments.Select(x => x.Id))
        .Concat(Draft.Cards.Select(x => x.Id));

    private IEnumerable<string> SegmentDependencies(string id) =>
        Draft.Chains.Where(chain => chain.SegmentIds.Contains(id)).Select(chain => $"цепочка {chain.Id}");

    private IEnumerable<string> NodeDependencies(string id, RouteSegmentConfiguration? owner = null) =>
        Draft.Segments.Where(line => line != owner && (line.FromNodeId == id || line.ToNodeId == id)).Select(line => $"линия {line.Id}")
            .Concat(Draft.Chains.Where(chain => chain.NodeIds.Contains(id)).Select(chain => $"цепочка {chain.Id}"))
            .Concat(Draft.Cards.Where(card => card.VerticalAnchorNodeId == id).Select(card => $"карточка {card.Id}"));

    private bool ValidateEditedFragmentCounts()
    {
        foreach (var segment in _segmentsWithChangedKind.Where(line => line.Kind != RouteSegmentKind.DeadEnd && line.ActiveFragments.Count > 0))
        {
            var count = RouteSegmentActiveFragmentSynchronizer.CalculateFragmentCount(Draft, segment);
            if (count > 1 && segment.ActiveFragments.All(fragment => fragment.Index <= count))
                continue;

            ErrorMessage = $"Линия {segment.Id}: измените геометрию или настройки деления, чтобы сохранить существующие сигналы отрезков.";
            StatusMessage = null;
            return false;
        }
        return true;
    }

    private bool BlockDelete(IEnumerable<string> dependencies)
    {
        var items = dependencies.Distinct().ToArray();
        if (items.Length == 0) return false;
        ErrorMessage = "Удаление заблокировано. Сначала удалите ссылки: " + string.Join(", ", items);
        return true;
    }

    private void AddFrequencyBinding()
    {
        FrequencyBinding ??= new SignalBindingConfiguration
        {
            Role = SignalBindingRole.FrequencyFeedback,
            Direction = SignalBindingDirection.Read,
            ValueType = SignalValueType.Float32,
        };
    }

    private void AddBinding(string scope)
    {
        var topBar = Draft.TopBar;
        var addResetFeedback = scope.EndsWith("-reset-feedback", StringComparison.Ordinal);
        if (addResetFeedback)
            scope = scope[..^"-reset-feedback".Length];

        (RouteTopBarButtonConfiguration? Button, SignalBindingRole CommandRole) topBarSection = scope switch
        {
            "unload" => (topBar.Unload, SignalBindingRole.UnloadCommand),
            "rotation" => (topBar.Rotation, SignalBindingRole.RotationCommand),
            "load" => (topBar.Load, SignalBindingRole.LoadCommand),
            "reset" => (topBar.Reset, SignalBindingRole.ResetCommand),
            "emergency" => (topBar.Emergency, SignalBindingRole.EmergencyCommand),
            _ => default,
        };
        if (topBarSection.Button is { } button)
        {
            var role = addResetFeedback ? SignalBindingRole.CommandResetFeedback : SignalBindingRole.Enabled;
            if (button.Bindings.Any(x => x?.Role == role))
                return;
            var command = button.Bindings.FirstOrDefault(x => x?.Role == topBarSection.CommandRole);
            if (command is null)
                return;

            button.Bindings.Add(new SignalBindingConfiguration
            {
                Role = role,
                SignalId = addResetFeedback ? $"{command.SignalId}.reset" : $"{command.SignalId}.enabled",
                Direction = SignalBindingDirection.Read,
                ValueType = SignalValueType.Bool,
            });
            return;
        }

        if (addResetFeedback)
        {
            if (scope == "card" && SelectedCard is { CanStart: true, StartButtonKind: RouteCommandButtonKind.Momentary } card
                && !card.Bindings.Any(binding => binding.Role == SignalBindingRole.CommandResetFeedback)
                && card.Bindings.FirstOrDefault(binding => binding.Role == SignalBindingRole.StartCommand) is { } start)
            {
                card.Bindings.Add(new SignalBindingConfiguration
                {
                    Role = SignalBindingRole.CommandResetFeedback,
                    SignalId = $"{start.SignalId}.reset",
                    Direction = SignalBindingDirection.Read,
                    ValueType = SignalValueType.Bool,
                });
            }
            return;
        }

        var bindings = scope switch
        {
            "node" => SelectedNode?.Bindings,
            "segment" => SelectedSegment?.Bindings,
            "card" => SelectedCard?.Bindings,
            _ => null,
        };
        if (bindings is null)
            return;

        bindings.Add(new SignalBindingConfiguration
        {
            Role = scope == "segment" && IsDeadEnd ? SignalBindingRole.Visible : SignalBindingRole.State,
            SignalId = "signal.new",
            Direction = SignalBindingDirection.Read,
            ValueType = SignalValueType.Bool,
        });
    }

    private void RemoveBinding(SignalBindingConfiguration binding)
    {
        // Historical mode settings are retained only for JSON compatibility.
        if (Draft.TopBar.Automatic.Bindings.Contains(binding) || Draft.TopBar.Manual.Bindings.Contains(binding))
            return;

        if (IsRequiredBinding(binding))
        {
            ErrorMessage = $"Binding {binding.Role} обязателен для текущего объекта и не может быть удален.";
            return;
        }
        SelectedNode?.Bindings.Remove(binding);
        SelectedSegment?.Bindings.Remove(binding);
        SelectedCard?.Bindings.Remove(binding);
        Draft.TopBar.Unload.Bindings.Remove(binding);
        Draft.TopBar.Rotation.Bindings.Remove(binding);
        Draft.TopBar.Load.Bindings.Remove(binding);
        Draft.TopBar.Reset.Bindings.Remove(binding);
        Draft.TopBar.Emergency.Bindings.Remove(binding);
    }

    private void AddStatus()
    {
        if (SelectedCard?.StatusMapping?.Entries is not { } entries)
            return;

        if (entries.Count >= 256)
        {
            ErrorMessage = "Таблица статусов может содержать до 256 строк.";
            return;
        }

        var usedCodes = entries.Where(entry => entry is not null).Select(entry => entry.Code).ToHashSet();
        var code = 0;
        while (usedCodes.Contains(code))
            code++;

        entries.Add(new EquipmentStatusEntryConfiguration { Code = code, Color = "#77828D" });
    }

    private void RemoveStatus(EquipmentStatusEntryConfiguration entry) =>
        SelectedCard?.StatusMapping?.Entries?.Remove(entry);

    private void ObserveSelectedStatusMapping()
    {
        _statusMappingSubscriptions.Disposable = null;
        if (SelectedCard is not { StatusMapping: { } mapping } card)
            return;

        var subscriptions = new CompositeDisposable();
        _statusMappingSubscriptions.Disposable = subscriptions;

        void Validate() => UpdateStatusValidation(card, mapping);
        void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ObserveSelectedStatusMapping();

        card.Bindings.CollectionChanged += CollectionChanged;
        subscriptions.Add(Disposable.Create(() => card.Bindings.CollectionChanged -= CollectionChanged));
        foreach (var binding in card.Bindings)
            subscriptions.Add(binding.Changed.Subscribe(_ => Validate()));

        subscriptions.Add(mapping.Changed
            .Where(change => change.PropertyName is nameof(mapping.UnknownText) or nameof(mapping.UnavailableText))
            .Subscribe(_ => Validate()));

        if (mapping.Entries is { } entries)
        {
            entries.CollectionChanged += CollectionChanged;
            subscriptions.Add(Disposable.Create(() => entries.CollectionChanged -= CollectionChanged));
            foreach (var entry in entries)
            {
                if (entry is not null)
                    subscriptions.Add(entry.Changed
                        .Where(change => change.PropertyName is nameof(entry.Code) or nameof(entry.Text)
                            or nameof(entry.Color) or nameof(entry.Category) or nameof(entry.BlockStart) or nameof(entry.BlockStop))
                        .Subscribe(_ => Validate()));
            }
        }

        Validate();
    }

    private static void UpdateStatusValidation(EquipmentCardConfiguration card, EquipmentStatusMappingConfiguration mapping)
    {
        const string statusPath = nameof(EquipmentCardConfiguration.StatusMapping);
        const string entriesPath = statusPath + "." + nameof(EquipmentStatusMappingConfiguration.Entries);
        var errors = new List<RouteMapConfigurationError>();
        RouteMapConfigurationValidator.ValidateCardStatusMapping(errors, card);
        mapping.ValidationMessage = string.Join(Environment.NewLine, errors
            .Where(error => !error.Property.StartsWith(entriesPath + "[", StringComparison.Ordinal))
            .Select(error => error.Property switch
            {
                statusPath + "." + nameof(mapping.UnknownText) => $"Текст неизвестного статуса. {error.Message}",
                statusPath + "." + nameof(mapping.UnavailableText) => $"Текст недостоверного статуса. {error.Message}",
                _ => error.Message,
            }));

        if (mapping.Entries is not { } entries)
            return;

        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index] is not { } entry)
                continue;

            var rowPath = $"{entriesPath}[{index}]";
            entry.ValidationMessage = string.Join(Environment.NewLine, errors
                .Where(error => error.Property == rowPath || error.Property.StartsWith(rowPath + ".", StringComparison.Ordinal))
                .Select(error => error.Message));
        }
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
            ValueType = SignalValueType.Word,
        });
    }

    private void RemoveCardParameter(EquipmentCardParameterConfiguration parameter)
    {
        SelectedCard?.Parameters.Remove(parameter);
    }

    private bool IsRequiredBinding(SignalBindingConfiguration binding)
    {
        if (Draft.TopBar.Automatic.Bindings.Contains(binding))
            return binding.Role == SignalBindingRole.AutomaticModeCommand;
        if (Draft.TopBar.Manual.Bindings.Contains(binding))
            return binding.Role == SignalBindingRole.ManualModeCommand;
        if (Draft.TopBar.Unload.Bindings.Contains(binding))
            return binding.Role == SignalBindingRole.UnloadCommand;
        if (Draft.TopBar.Rotation.Bindings.Contains(binding))
            return binding.Role == SignalBindingRole.RotationCommand;
        if (Draft.TopBar.Load.Bindings.Contains(binding))
            return binding.Role == SignalBindingRole.LoadCommand;
        if (Draft.TopBar.Reset.Bindings.Contains(binding))
            return binding.Role == SignalBindingRole.ResetCommand;
        if (Draft.TopBar.Emergency.Bindings.Contains(binding))
            return binding.Role == SignalBindingRole.EmergencyCommand;
        if (SelectedNode?.Bindings.Contains(binding) == true)
            return binding.Role == SignalBindingRole.ActiveRoute
                || binding.Role == SignalBindingRole.TargetCommand && SelectedNode.MenuKind is RouteNodeMenuKind.SendOnly or RouteNodeMenuKind.SendAndReturn
                || binding.Role == SignalBindingRole.LoaderCommand && SelectedNode.MenuKind == RouteNodeMenuKind.SendAndReturn;
        if (SelectedSegment?.Bindings.Contains(binding) == true)
            return binding.Role == SignalBindingRole.ActiveRoute;
        if (SelectedCard?.Bindings.Contains(binding) == true)
            return binding.Role == SignalBindingRole.StartCommand && SelectedCard.CanStart
                || binding.Role == SignalBindingRole.StopCommand && SelectedCard.CanStop
                || binding.Role == SignalBindingRole.PowerFeedback && UsesPowerFeedback(SelectedCard)
                || (binding.Role is SignalBindingRole.UncheckedCommand or SignalBindingRole.CheckedCommand) && !UsesPowerFeedback(SelectedCard);
        return false;
    }

    private static void EnsureNodeBindings(RouteNodeConfiguration node)
    {
        if (node.Kind == RouteNodeKind.TerminalAnchor)
            return;

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
        card.StopButtonKind = RouteCommandButtonKind.Toggle;

        if (card.CanStart)
            EnsureBinding(card.Bindings, SignalBindingRole.StartCommand, $"{card.Id}.start", SignalBindingDirection.ReadWrite);

        if (card.CanStop)
            EnsureBinding(card.Bindings, SignalBindingRole.StopCommand, $"{card.Id}.stop", SignalBindingDirection.ReadWrite);

        if (UsesPowerFeedback(card))
        {
            RemoveBindings(card.Bindings, SignalBindingRole.UncheckedCommand, SignalBindingRole.CheckedCommand);
            EnsureBinding(card.Bindings, SignalBindingRole.PowerFeedback, $"{card.Id}.power", SignalBindingDirection.Read);
        }
        else
        {
            EnsureBinding(card.Bindings, SignalBindingRole.UncheckedCommand, $"{card.Id}.selector.off", SignalBindingDirection.ReadWrite);
            EnsureBinding(card.Bindings, SignalBindingRole.CheckedCommand, $"{card.Id}.selector.on", SignalBindingDirection.ReadWrite);
        }

        NormalizeOptionalOffFeedback(card.Bindings, SignalBindingRole.StartOffFeedback);
        NormalizeOptionalOffFeedback(card.Bindings, SignalBindingRole.StopOffFeedback);
        card.StartOffFeedbackEnabled = card.Bindings.Any(x => x.Role == SignalBindingRole.StartOffFeedback);
        card.StopOffFeedbackEnabled = card.Bindings.Any(x => x.Role == SignalBindingRole.StopOffFeedback);
        foreach (var parameter in card.Parameters)
            parameter.Role = SignalBindingRole.EquipmentParameter;
        RemoveBindings(card.Bindings, SignalBindingRole.State);
    }

    private static bool UsesPowerFeedback(EquipmentCardConfiguration card) =>
        card.StartButtonKind == RouteCommandButtonKind.Momentary ||
        card.Bindings.Any(binding => binding.Role == SignalBindingRole.PowerFeedback);

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
        EnsureNewTopBarCommandBindings(topBar);
        EnsureBinding(topBar.Reset.Bindings, SignalBindingRole.ResetCommand, "system.reset", SignalBindingDirection.ReadWrite);
        EnsureBinding(topBar.Emergency.Bindings, SignalBindingRole.EmergencyCommand, "system.emergency", SignalBindingDirection.ReadWrite);
        topBar.Emergency.ButtonKind = RouteCommandButtonKind.Toggle;
        topBar.Emergency.OffFeedbackEnabled = false;
        RemoveBindings(topBar.Emergency.Bindings, SignalBindingRole.EmergencyOffFeedback);
    }

    private static void EnsureNewTopBarCommandBindings(RouteTopBarConfiguration topBar)
    {
        EnsureTopBarCommandBinding(topBar.Unload, RouteMapSeed.DefaultUnloadButton.Binding);
        EnsureTopBarCommandBinding(topBar.Rotation, RouteMapSeed.DefaultRotationButton.Binding);
        EnsureTopBarCommandBinding(topBar.Load, RouteMapSeed.DefaultLoadButton.Binding);
    }

    private static void EnsureTopBarCommandBinding(RouteTopBarButtonConfiguration button, SignalBinding binding) =>
        EnsureBinding(button.Bindings, binding.Role, binding.SignalId, binding.Direction);

    private static void EnsureBinding(
        ICollection<SignalBindingConfiguration> bindings,
        SignalBindingRole role,
        string signalId,
        SignalBindingDirection direction)
    {
        if (bindings.Any(x => x?.Role == role))
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
                SignalBindingRole.PowerFeedback => $"{card.Id}.power",
                SignalBindingRole.UncheckedCommand => $"{card.Id}.selector.off",
                SignalBindingRole.CheckedCommand => $"{card.Id}.selector.on",
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
        if (segment.Kind == RouteSegmentKind.DeadEnd || segment.Bindings.Any(x => x.Role == SignalBindingRole.ActiveRoute))
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
        this.RaisePropertyChanged(nameof(FrequencyBinding));
        AttachDraft(_draft);
        SelectFirstItems();
        RaiseFilteredCollections();
    }

    private void AttachDraft(RouteMapConfigurationDocument document)
    {
        EnsureNewTopBarCommandBindings(document.TopBar);
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
        _replacedEndpoints.Clear();
        _segmentsWithChangedKind.Clear();
    }

    private void OnTopBarEmergencyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RouteTopBarEmergencyButtonConfiguration.ButtonKind)
            or nameof(RouteTopBarEmergencyButtonConfiguration.OffFeedbackEnabled))
            EnsureTopBarBindings(Draft.TopBar);
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (RouteMapConfigurationItem item in e.OldItems)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
                _knownIds.Remove(item);
                if (item is RouteSegmentConfiguration segment)
                {
                    _replacedEndpoints.Remove(segment);
                    _segmentsWithChangedKind.Remove(segment);
                }
            }
        }
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
        if (ReferenceEquals(sender, SelectedCard) && e.PropertyName == nameof(EquipmentCardConfiguration.StatusMapping))
            ObserveSelectedStatusMapping();

        if (sender is RouteNodeConfiguration node && e.PropertyName == nameof(RouteNodeConfiguration.MenuKind))
            EnsureNodeBindings(node);
        if (sender is EquipmentCardConfiguration changedCard &&
            e.PropertyName is nameof(EquipmentCardConfiguration.CanStart)
                or nameof(EquipmentCardConfiguration.CanStop)
                or nameof(EquipmentCardConfiguration.StartButtonKind)
                or nameof(EquipmentCardConfiguration.StopButtonKind)
                or nameof(EquipmentCardConfiguration.StartOffFeedbackEnabled)
                or nameof(EquipmentCardConfiguration.StopOffFeedbackEnabled))
        {
            EnsureCardBindings(changedCard);
            if (ReferenceEquals(changedCard, SelectedCard))
            {
                this.RaisePropertyChanged(nameof(CardBindingRoles));
                this.RaisePropertyChanged(nameof(CanAddStartResetFeedback));
            }
        }
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
        SelectedNode = OrdinaryNodes.FirstOrDefault();
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

    private void RaiseSegmentEditorProperties()
    {
        this.RaisePropertyChanged(nameof(SelectedSegmentKind));
        this.RaisePropertyChanged(nameof(SelectedTerminalSide));
        this.RaisePropertyChanged(nameof(IsDeadEnd));
        this.RaisePropertyChanged(nameof(IsElbow));
        this.RaisePropertyChanged(nameof(IsFromTerminalAnchor));
        this.RaisePropertyChanged(nameof(IsToTerminalAnchor));
        this.RaisePropertyChanged(nameof(SelectedTerminalAnchor));
        this.RaisePropertyChanged(nameof(CanEditSegmentActivity));
        this.RaisePropertyChanged(nameof(SegmentBindingRoles));
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

public sealed record RouteMapEditorChoice<T>(T Value, string Label);
