using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Media;
using Avalonia.Threading;
using Configurator.Application.Services;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Microsoft.Extensions.Options;
using ReactiveUI;

namespace Configurator.Desktop.Workspace.RouteMap.SignalMapping;

public sealed class RouteMapSignalMappingViewModel : ViewModelBase, IDisposable
{
    private readonly RouteMapConfigurationManager _configurationManager;
    private readonly IOptionsMonitor<ModbusOptions> _optionsMonitor;
    private readonly IAppConfigService _appConfigService;
    private readonly IModbusDataMapValidator _validator;
    private readonly IModbusDataMapRuntime _dataMapRuntime;
    private readonly Dictionary<RouteMapSignalElementCategory, bool> _groupExpansionStates = [];
    private readonly IDisposable _definitionSubscription;
    private readonly IDisposable? _optionsSubscription;
    private ModbusOptions _baseOptions;
    private ModbusOptions _addressOptions;
    private bool _isLoading;
    private bool _isDirty;
    private bool _hasExternalChanges;
    private string? _errorMessage;
    private string? _statusMessage;
    private bool _disposed;

    public RouteMapSignalMappingViewModel(
        RouteMapConfigurationManager configurationManager,
        IOptionsMonitor<ModbusOptions> optionsMonitor,
        IAppConfigService appConfigService,
        IModbusDataMapValidator validator,
        IModbusDataMapRuntime dataMapRuntime)
    {
        _configurationManager = configurationManager;
        _optionsMonitor = optionsMonitor;
        _appConfigService = appConfigService;
        _validator = validator;
        _dataMapRuntime = dataMapRuntime;
        _baseOptions = optionsMonitor.CurrentValue.Clone();
        _addressOptions = ReadAddressOptions();

        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        ReloadCommand = ReactiveCommand.Create(Reload);
        CreateMappingCommand = ReactiveCommand.Create<RouteMapSignalMappingRow>(CreateMapping);
        RemoveMappingCommand = ReactiveCommand.Create<RouteMapSignalMappingRow>(RemoveMapping);

        RebuildRows(configurationManager.CurrentDefinition, _baseOptions, preserveDraft: false);
        _definitionSubscription = configurationManager.DefinitionChanges
            .Skip(1)
            .Subscribe(definition => Dispatcher.UIThread.Post(() =>
                RebuildRows(definition, _baseOptions, preserveDraft: IsDirty)));
        _optionsSubscription = optionsMonitor.OnChange((options, name) =>
            Dispatcher.UIThread.Post(() => HandleExternalOptions(options, name)));
    }

    public ObservableCollection<RouteMapSignalMappingRow> Rows { get; } = [];
    public ObservableCollection<RouteMapSignalMappingGroup> Groups { get; } = [];
    public IReadOnlyList<ModbusDataArea> DataAreas { get; } = Enum.GetValues<ModbusDataArea>();
    public IReadOnlyList<ModbusDataAccess> AccessModes { get; } = Enum.GetValues<ModbusDataAccess>();
    public IReadOnlyList<ModbusValueType> ValueTypes { get; } = Enum.GetValues<ModbusValueType>();
    public IReadOnlyList<ModbusWriteMode> WriteModes { get; } = Enum.GetValues<ModbusWriteMode>();
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> ReloadCommand { get; }
    public ReactiveCommand<RouteMapSignalMappingRow, Unit> CreateMappingCommand { get; }
    public ReactiveCommand<RouteMapSignalMappingRow, Unit> RemoveMappingCommand { get; }

    public bool IsDirty
    {
        get => _isDirty;
        private set => this.RaiseAndSetIfChanged(ref _isDirty, value);
    }

    public bool HasExternalChanges
    {
        get => _hasExternalChanges;
        private set => this.RaiseAndSetIfChanged(ref _hasExternalChanges, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _errorMessage, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public string ClientAddressBase =>
        $"Client: Coil +{_addressOptions.Client.CoilStartAddress}, Holding Register +{_addressOptions.Client.HoldingRegisterStartAddress}";

    public string ServerAddressBase =>
        $"Server: Coil +{_addressOptions.Server.CoilStartAddress}, Holding Register +{_addressOptions.Server.HoldingRegisterStartAddress}";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _definitionSubscription.Dispose();
        _optionsSubscription?.Dispose();
        DetachRows();
    }

    private void CreateMapping(RouteMapSignalMappingRow row)
    {
        if (row.IsSystem)
        {
            return;
        }

        row.ResetToDefaults();
        row.IsMapped = true;
        ValidateRow(row);
    }

    private void RemoveMapping(RouteMapSignalMappingRow row)
    {
        if (!row.IsSystem)
        {
            row.IsMapped = false;
            ValidateRow(row);
        }
    }

    internal async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        ErrorMessage = null;
        StatusMessage = null;
        foreach (var row in Rows)
        {
            ValidateRow(row);
        }

        var invalidRows = Rows.Where(row => row.HasError).ToArray();
        if (invalidRows.Length > 0)
        {
            ErrorMessage = string.Join(Environment.NewLine, invalidRows.Take(8).Select(row =>
                $"{row.SignalId}: {row.ValidationMessage}"));
            return;
        }

        var latest = _optionsMonitor.CurrentValue.Clone();
        var routeSignalIds = Rows
            .Where(row => !row.IsSystem)
            .Select(row => row.SignalId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        latest.DataMap = latest.DataMap
            .Where(point => !routeSignalIds.Contains(point.Name))
            .Select(point => point.Clone())
            .Concat(Rows.Where(row => row.IsMapped && !row.IsSystem).Select(row => row.ToOptions()))
            .ToList();

        var validation = ValidateOptions(latest);
        if (!validation.Succeeded)
        {
            ErrorMessage = OperationMessage(validation);
            return;
        }

        try
        {
            await _appConfigService.SaveSectionAsync(ModbusOptions.SectionName, latest, cancellationToken);
            _baseOptions = latest.Clone();
            _addressOptions = ReadAddressOptions();
            IsDirty = false;
            HasExternalChanges = false;
            RefreshPhysicalAddresses();

            var applyResult = _dataMapRuntime.ApplyDataMap(latest.DataMap);
            if (!applyResult.Succeeded)
            {
                ErrorMessage = $"Карта сохранена, но не применена к работающему Modbus: {OperationMessage(applyResult)} Она вступит в силу после перезапуска runtime.";
                return;
            }

            StatusMessage = "Связи SignalId сохранены и применены к Modbus runtime.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Не удалось сохранить связи SignalId: {ex.Message}";
        }
    }

    private void Reload()
    {
        RebuildRows(_configurationManager.CurrentDefinition, _optionsMonitor.CurrentValue.Clone(), preserveDraft: false);
        ErrorMessage = null;
        StatusMessage = "Связи перечитаны из Modbus.DataMap.";
        HasExternalChanges = false;
    }

    private void HandleExternalOptions(ModbusOptions options, string? name)
    {
        if (string.Equals(name, ModbusOptions.DemoSectionName, StringComparison.Ordinal))
        {
            RefreshPhysicalAddresses();
            return;
        }

        if (OptionsEquivalent(options, _baseOptions))
        {
            return;
        }

        if (IsDirty)
        {
            HasExternalChanges = true;
            StatusMessage = "Modbus.DataMap изменен извне. Нажмите ПЕРЕЗАГРУЗИТЬ, чтобы отказаться от локального черновика.";
            return;
        }

        RebuildRows(_configurationManager.CurrentDefinition, options.Clone(), preserveDraft: false);
        StatusMessage = "Modbus.DataMap обновлен из конфигурации.";
    }

    internal void RebuildRows(
        RouteMapDefinition definition,
        ModbusOptions options,
        bool preserveDraft)
    {
        var drafts = preserveDraft
            ? Rows.Where(row => !row.IsSystem).ToDictionary(row => row.SignalId, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, RouteMapSignalMappingRow>(StringComparer.OrdinalIgnoreCase);
        var inventory = RouteMapSignalInventory.Build(definition);
        foreach (var group in Groups)
        {
            _groupExpansionStates[group.Category] = group.IsExpanded;
        }

        _isLoading = true;
        try
        {
            DetachRows();
            Rows.Clear();
            Groups.Clear();
            _baseOptions = options.Clone();
            _addressOptions = ReadAddressOptions();
            foreach (var item in inventory)
            {
                RouteMapSignalMappingRow row;
                if (drafts.TryGetValue(item.SignalId, out var draft))
                {
                    row = new RouteMapSignalMappingRow(item, draft.ToOptions(), draft.IsMapped);
                }
                else
                {
                    var point = options.DataMap.FirstOrDefault(candidate =>
                        string.Equals(candidate.Name, item.SignalId, StringComparison.OrdinalIgnoreCase));
                    row = new RouteMapSignalMappingRow(
                        item,
                        point?.Clone() ?? RouteMapSignalMappingRow.CreateDefaultPoint(item),
                        point is not null);
                }

                row.UpdateAddressBases(_addressOptions.Client, _addressOptions.Server);
                row.PropertyChanged += OnRowPropertyChanged;
                ValidateRow(row);
                Rows.Add(row);
            }

            BuildGroups();

            this.RaisePropertyChanged(nameof(ClientAddressBase));
            this.RaisePropertyChanged(nameof(ServerAddressBase));
            if (!preserveDraft)
            {
                IsDirty = false;
                HasExternalChanges = false;
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (_isLoading
            || sender is not RouteMapSignalMappingRow row
            || eventArgs.PropertyName is not (
                nameof(RouteMapSignalMappingRow.IsMapped)
                or nameof(RouteMapSignalMappingRow.Area)
                or nameof(RouteMapSignalMappingRow.Address)
                or nameof(RouteMapSignalMappingRow.Length)
                or nameof(RouteMapSignalMappingRow.Access)
                or nameof(RouteMapSignalMappingRow.Type)
                or nameof(RouteMapSignalMappingRow.BitIndex)
                or nameof(RouteMapSignalMappingRow.WriteMode)
                or nameof(RouteMapSignalMappingRow.PulseDurationMs)
                or nameof(RouteMapSignalMappingRow.PhysicalAddressError)))
        {
            return;
        }

        IsDirty = true;
        StatusMessage = null;
        row.UpdateAddressBases(_addressOptions.Client, _addressOptions.Server);
        ValidateRow(row);
    }

    private void ValidateRow(RouteMapSignalMappingRow row)
    {
        if (row.IsSystem)
        {
            row.SetValidation(null);
            return;
        }

        if (row.HasTypeConflict)
        {
            row.SetValidation("Один SignalId используется с разными SignalValueType.");
            return;
        }

        if (!row.IsMapped)
        {
            row.SetValidation(null);
            return;
        }

        if (!string.IsNullOrWhiteSpace(row.PhysicalAddressError))
        {
            row.SetValidation(row.PhysicalAddressError);
            return;
        }

        if (!TypesMatch(row.ExpectedType, row.Type))
        {
            row.SetValidation($"Ожидается {row.ExpectedType}, выбран несовместимый Modbus тип {row.Type}.");
            return;
        }

        if (!AccessSatisfies(row.RequiredAccess, row.Access))
        {
            row.SetValidation($"Binding требует доступ {row.RequiredAccess}, выбран {row.Access}.");
            return;
        }

        if (row.RequiresLatchedWriteMode && row.WriteMode != ModbusWriteMode.Latched)
        {
            row.SetValidation("Selector-команды требуют режим записи Latched.");
            return;
        }

        if (row.RequiresPulseWriteMode && row.WriteMode != ModbusWriteMode.Pulse)
        {
            row.SetValidation("ResetCommand требует режим записи Pulse.");
            return;
        }

        var singlePointOptions = CreateValidationOptions(_baseOptions);
        singlePointOptions.DataMap = [row.ToOptions()];
        var validation = _validator.Validate(singlePointOptions, ModbusRunMode.None);
        row.SetValidation(validation.Succeeded ? null : OperationMessage(validation));
    }

    private ModbusOperationResult ValidateOptions(ModbusOptions options)
    {
        var validationOptions = CreateValidationOptions(options);
        var structural = _validator.Validate(validationOptions, ModbusRunMode.None);
        if (!structural.Succeeded)
        {
            return structural;
        }

        if (validationOptions.Client.Enabled)
        {
            var client = _validator.Validate(validationOptions, ModbusRunMode.Client);
            if (!client.Succeeded)
            {
                return client;
            }
        }

        return validationOptions.Server.Enabled
            ? _validator.Validate(validationOptions, ModbusRunMode.Server)
            : ModbusOperationResult.Success();
    }

    private void RefreshPhysicalAddresses()
    {
        _addressOptions = ReadAddressOptions();
        foreach (var row in Rows)
        {
            row.UpdateAddressBases(_addressOptions.Client, _addressOptions.Server);
        }

        this.RaisePropertyChanged(nameof(ClientAddressBase));
        this.RaisePropertyChanged(nameof(ServerAddressBase));
    }

    private void DetachRows()
    {
        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }
    }

    private void BuildGroups()
    {
        foreach (var category in RouteMapSignalMappingGroup.DisplayOrder)
        {
            var rows = Rows.Where(row => row.Category == category).ToArray();
            if (rows.Length == 0)
            {
                continue;
            }

            var isExpanded = !_groupExpansionStates.TryGetValue(category, out var expanded) || expanded;
            Groups.Add(new RouteMapSignalMappingGroup(category, rows, isExpanded));
        }
    }

    private static bool TypesMatch(SignalValueType signalType, ModbusValueType modbusType) =>
        SignalModbusTypeCompatibility.IsCompatible(signalType, modbusType);

    private static bool AccessSatisfies(ModbusDataAccess required, ModbusDataAccess actual) =>
        required switch
        {
            ModbusDataAccess.Read => actual is ModbusDataAccess.Read or ModbusDataAccess.ReadWrite,
            ModbusDataAccess.Write => actual is ModbusDataAccess.Write or ModbusDataAccess.ReadWrite,
            ModbusDataAccess.ReadWrite => actual == ModbusDataAccess.ReadWrite,
            _ => false,
        };

    private static string OperationMessage(ModbusOperationResult result) =>
        result.ErrorDetails is { Length: > 0 }
            ? $"{result.ErrorMessage} {result.ErrorDetails}"
            : result.ErrorMessage ?? "Ошибка Modbus.DataMap.";

    private ModbusOptions CreateValidationOptions(ModbusOptions routeOptions)
    {
        var options = routeOptions.Clone();
        var addressOptions = ReadAddressOptions();
        options.AutostartOnWorkspaceOpen = addressOptions.AutostartOnWorkspaceOpen;
        options.StartupMode = addressOptions.StartupMode;
        options.Client = addressOptions.Client.Clone();
        options.Server = addressOptions.Server.Clone();
        return options;
    }

    private ModbusOptions ReadAddressOptions()
        => _optionsMonitor.Get(ModbusOptions.DemoSectionName).Clone();

    private static bool OptionsEquivalent(ModbusOptions left, ModbusOptions right)
    {
        if (left.WriteConfirmationTimeoutMs != right.WriteConfirmationTimeoutMs
            || left.DataMap.Count != right.DataMap.Count)
        {
            return false;
        }

        return left.DataMap.Zip(right.DataMap).All(pair =>
            pair.First.Name == pair.Second.Name
            && pair.First.Area == pair.Second.Area
            && pair.First.Address == pair.Second.Address
            && pair.First.Length == pair.Second.Length
            && pair.First.Access == pair.Second.Access
            && pair.First.Type == pair.Second.Type
            && pair.First.BitIndex == pair.Second.BitIndex
            && pair.First.WriteMode == pair.Second.WriteMode
            && pair.First.PulseDurationMs == pair.Second.PulseDurationMs);
    }

}

public sealed class RouteMapSignalMappingRow : ReactiveObject
{
    private static readonly IBrush UsedRowBackground = Brushes.White;
    private static readonly IBrush UnusedRowBackground = new SolidColorBrush(Color.Parse("#F1F3F5"));

    private bool _isMapped;
    private ModbusDataArea _area;
    private int _address;
    private int _length;
    private ModbusDataAccess _access;
    private ModbusValueType _type;
    private int? _bitIndex;
    private ModbusWriteMode _writeMode;
    private int _pulseDurationMs;
    private string? _validationMessage;
    private string? _physicalAddressError;
    private ModbusEndpointOptions _clientEndpoint = new();
    private ModbusEndpointOptions _serverEndpoint = new();
    private string _clientPhysicalAddress = string.Empty;
    private string _serverPhysicalAddress = string.Empty;

    internal RouteMapSignalMappingRow(
        RouteMapSignalInventoryItem inventory,
        ModbusDataPointOptions point,
        bool isMapped)
    {
        SignalId = inventory.SignalId;
        ExpectedType = inventory.ExpectedType;
        RequiredAccess = inventory.RequiredAccess;
        Roles = inventory.Roles;
        Objects = inventory.Objects;
        HasTypeConflict = inventory.HasTypeConflict;
        Category = inventory.Category;
        IsSystem = inventory.IsSystem;
        PreferPulseWriteMode = inventory.PreferPulseWriteMode;
        PreferHoldingRegisterBit = inventory.PreferHoldingRegisterBit;
        PreferredBitIndex = inventory.PreferredBitIndex;
        RequiresLatchedWriteMode = inventory.RequiresLatchedWriteMode;
        RequiresPulseWriteMode = inventory.RequiresPulseWriteMode;
        AvailableValueTypes = SignalModbusTypeCompatibility.CompatibleModbusTypes(ExpectedType);
        _isMapped = isMapped;
        ApplyPoint(point);
    }

    public string SignalId { get; }
    public SignalValueType ExpectedType { get; }
    public ModbusDataAccess RequiredAccess { get; }
    public string Roles { get; }
    public string Objects { get; }
    public bool HasTypeConflict { get; }
    internal RouteMapSignalElementCategory Category { get; }
    public bool IsSystem { get; }
    public bool PreferPulseWriteMode { get; }
    public bool PreferHoldingRegisterBit { get; }
    public int? PreferredBitIndex { get; }
    public bool RequiresLatchedWriteMode { get; }
    public bool RequiresPulseWriteMode { get; }
    public IReadOnlyList<ModbusValueType> AvailableValueTypes { get; }

    public bool IsMapped { get => _isMapped; set { this.RaiseAndSetIfChanged(ref _isMapped, value); RaiseStatus(); } }

    public ModbusDataArea Area
    {
        get => _area;
        set
        {
            if (_area == value)
            {
                return;
            }

            ClearPhysicalAddressError();
            this.RaiseAndSetIfChanged(ref _area, value);
            NormalizePointShape();
            RaiseStatus();
        }
    }

    public int Address
    {
        get => _address;
        set
        {
            ClearPhysicalAddressError();
            this.RaiseAndSetIfChanged(ref _address, value);
            NormalizePointShape();
            RaiseStatus();
        }
    }

    public int Length
    {
        get => _length;
        set
        {
            this.RaiseAndSetIfChanged(ref _length, value);
            NormalizePointShape();
            RaiseStatus();
        }
    }
    public ModbusDataAccess Access { get => _access; set { this.RaiseAndSetIfChanged(ref _access, value); RaiseStatus(); } }

    public ModbusValueType Type
    {
        get => _type;
        set
        {
            if (_type == value)
            {
                return;
            }

            ClearPhysicalAddressError();
            this.RaiseAndSetIfChanged(ref _type, value);
            NormalizePointShape();
            RaiseStatus();
        }
    }

    public int? BitIndex
    {
        get => _bitIndex;
        set
        {
            int? next = UsesRegisterBit ? value ?? 0 : null;
            SetBitIndex(next);
        }
    }

    public ModbusWriteMode WriteMode { get => _writeMode; set { this.RaiseAndSetIfChanged(ref _writeMode, value); RaiseStatus(); } }
    public int PulseDurationMs { get => _pulseDurationMs; set { this.RaiseAndSetIfChanged(ref _pulseDurationMs, value); RaiseStatus(); } }
    public string? ValidationMessage => _validationMessage;
    public string? PhysicalAddressError => _physicalAddressError;
    public bool HasError => !string.IsNullOrWhiteSpace(ValidationMessage);
    public string StatusText => IsSystem ? "Системный" : HasError ? "Ошибка" : IsMapped ? "Настроен" : "Не настроен";
    public IBrush RowBackground => !IsSystem && !IsMapped ? UnusedRowBackground : UsedRowBackground;
    public bool CanCreateMapping => !IsSystem && !IsMapped;
    public bool CanRemoveMapping => !IsSystem && IsMapped;
    public bool CanEditMapping => !IsSystem && IsMapped;
    public bool CanEditBitIndex => CanEditMapping && UsesRegisterBit;
    public string ClientPhysicalAddress => _clientPhysicalAddress;
    public string ServerPhysicalAddress => _serverPhysicalAddress;

    public string ClientPhysicalAddressText
    {
        get => ClientPhysicalAddress;
        set => ApplyPhysicalAddress(value, _clientEndpoint);
    }

    public string ServerPhysicalAddressText
    {
        get => ServerPhysicalAddress;
        set => ApplyPhysicalAddress(value, _serverEndpoint);
    }

    public void ResetToDefaults() => ApplyPoint(CreateDefaultPoint(new RouteMapSignalInventoryItem(
        SignalId, ExpectedType, RequiredAccess, Roles, Objects, HasTypeConflict, Category, IsSystem,
        PreferPulseWriteMode, PreferHoldingRegisterBit, PreferredBitIndex, RequiresLatchedWriteMode, RequiresPulseWriteMode)));

    public ModbusDataPointOptions ToOptions() => new()
    {
        Name = SignalId,
        Area = Area,
        Address = Address,
        Length = Length,
        Access = Access,
        Type = Type,
        BitIndex = UsesRegisterBit ? BitIndex : null,
        WriteMode = WriteMode,
        PulseDurationMs = PulseDurationMs,
    };

    public void SetValidation(string? message)
    {
        this.RaiseAndSetIfChanged(ref _validationMessage, message, nameof(ValidationMessage));
        this.RaisePropertyChanged(nameof(HasError));
        this.RaisePropertyChanged(nameof(StatusText));
    }

    public void UpdateAddressBases(ModbusEndpointOptions client, ModbusEndpointOptions server)
    {
        _clientEndpoint = client.Clone();
        _serverEndpoint = server.Clone();
        _clientPhysicalAddress = FormatPhysicalAddress(_clientEndpoint);
        _serverPhysicalAddress = FormatPhysicalAddress(_serverEndpoint);
        this.RaisePropertyChanged(nameof(ClientPhysicalAddress));
        this.RaisePropertyChanged(nameof(ServerPhysicalAddress));
        this.RaisePropertyChanged(nameof(ClientPhysicalAddressText));
        this.RaisePropertyChanged(nameof(ServerPhysicalAddressText));
    }

    internal static ModbusDataPointOptions CreateDefaultPoint(RouteMapSignalInventoryItem item)
    {
        var type = SignalModbusTypeCompatibility.DefaultModbusType(item.ExpectedType);
        var length = SignalModbusTypeCompatibility.DefaultRegisterLength(item.ExpectedType);
        var area = type == ModbusValueType.Bool && !item.PreferHoldingRegisterBit
            ? ModbusDataArea.Coil
            : ModbusDataArea.HoldingRegister;

        return new ModbusDataPointOptions
        {
            Name = item.SignalId,
            Area = area,
            Address = 0,
            Length = length,
            Access = item.RequiredAccess,
            Type = type,
            BitIndex = item.PreferHoldingRegisterBit ? item.PreferredBitIndex ?? 0 : null,
            WriteMode = item.PreferPulseWriteMode ? ModbusWriteMode.Pulse : ModbusWriteMode.Latched,
            PulseDurationMs = 300,
        };
    }

    private void ApplyPoint(ModbusDataPointOptions point)
    {
        this.RaiseAndSetIfChanged(ref _area, point.Area, nameof(Area));
        this.RaiseAndSetIfChanged(ref _address, point.Address, nameof(Address));
        this.RaiseAndSetIfChanged(ref _length, point.Length, nameof(Length));
        this.RaiseAndSetIfChanged(ref _access, point.Access, nameof(Access));
        this.RaiseAndSetIfChanged(ref _type, point.Type, nameof(Type));
        SetBitIndex(UsesRegisterBit ? point.BitIndex ?? 0 : null);
        this.RaiseAndSetIfChanged(ref _writeMode, point.WriteMode, nameof(WriteMode));
        this.RaiseAndSetIfChanged(ref _pulseDurationMs, point.PulseDurationMs, nameof(PulseDurationMs));
        NormalizePointShape();
        RaiseStatus();
    }

    private string FormatPhysicalAddress(ModbusEndpointOptions endpoint)
    {
        if (IsSystem || !IsMapped)
        {
            return "—";
        }

        var physical = BaseAddress(endpoint) + Address;
        return physical.ToString(CultureInfo.InvariantCulture);
    }

    private void ApplyPhysicalAddress(string? value, ModbusEndpointOptions endpoint)
    {
        if (IsSystem || !IsMapped)
        {
            return;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var physical))
        {
            SetPhysicalAddressError($"Физический адрес '{value}' должен быть целым числом.");
            return;
        }

        if (ResolveArea(physical, endpoint) is not { } area)
        {
            SetPhysicalAddressError(
                $"Физический адрес {physical} не входит в диапазоны endpoint: " +
                $"Coil {FormatRange(endpoint.CoilStartAddress, endpoint.CoilCount)}, " +
                $"Holding Register {FormatRange(endpoint.HoldingRegisterStartAddress, endpoint.RegisterCount)}.");
            return;
        }

        ClearPhysicalAddressError();
        Area = area;
        Address = physical - BaseAddress(endpoint);
    }

    private int BaseAddress(ModbusEndpointOptions endpoint)
        => Area == ModbusDataArea.Coil
            ? endpoint.CoilStartAddress
            : endpoint.HoldingRegisterStartAddress;

    private bool UsesRegisterBit => Area == ModbusDataArea.HoldingRegister && Type == ModbusValueType.Bool;

    private ModbusDataArea? ResolveArea(int physical, ModbusEndpointOptions endpoint)
    {
        var isCoil = Contains(endpoint.CoilStartAddress, endpoint.CoilCount, physical);
        var isRegister = Contains(endpoint.HoldingRegisterStartAddress, endpoint.RegisterCount, physical);

        return (isCoil, isRegister) switch
        {
            (false, false) => null,
            (true, false) => ModbusDataArea.Coil,
            (false, true) => ModbusDataArea.HoldingRegister,
            (true, true) when Type != ModbusValueType.Bool => ModbusDataArea.HoldingRegister,
            (true, true) => Area,
        };
    }

    private static bool Contains(int start, int count, int value)
    {
        if (count <= 0)
        {
            return false;
        }

        var endExclusive = (long)start + count;
        return value >= start && value < endExclusive;
    }

    private static string FormatRange(int start, int count)
    {
        if (count <= 0)
        {
            return "disabled";
        }

        return $"{start}..{start + count - 1}";
    }

    private void NormalizePointShape()
    {
        NormalizeBitIndexForShape();

        var normalizedLength = Area == ModbusDataArea.Coil
            ? 1
            : SignalModbusTypeCompatibility.NormalizeRegisterLength(Type, _length);
        if (_length != normalizedLength)
        {
            this.RaiseAndSetIfChanged(ref _length, normalizedLength, nameof(Length));
        }
    }

    private void NormalizeBitIndexForShape()
        => SetBitIndex(UsesRegisterBit ? _bitIndex ?? 0 : null);

    private void SetPhysicalAddressError(string? message)
    {
        if (_physicalAddressError == message)
        {
            return;
        }

        this.RaiseAndSetIfChanged(ref _physicalAddressError, message, nameof(PhysicalAddressError));
        RaiseStatus();
    }

    private void ClearPhysicalAddressError() => SetPhysicalAddressError(null);

    private void SetBitIndex(int? value)
    {
        if (_bitIndex == value)
        {
            return;
        }

        this.RaiseAndSetIfChanged(ref _bitIndex, value, nameof(BitIndex));
        RaiseStatus();
    }

    private void RaiseStatus()
    {
        this.RaisePropertyChanged(nameof(StatusText));
        this.RaisePropertyChanged(nameof(RowBackground));
        this.RaisePropertyChanged(nameof(CanCreateMapping));
        this.RaisePropertyChanged(nameof(CanRemoveMapping));
        this.RaisePropertyChanged(nameof(CanEditMapping));
        this.RaisePropertyChanged(nameof(CanEditBitIndex));
        this.RaisePropertyChanged(nameof(ClientPhysicalAddress));
        this.RaisePropertyChanged(nameof(ServerPhysicalAddress));
        this.RaisePropertyChanged(nameof(ClientPhysicalAddressText));
        this.RaisePropertyChanged(nameof(ServerPhysicalAddressText));
    }
}

public sealed class RouteMapSignalMappingGroup : ReactiveObject
{
    internal static IReadOnlyList<RouteMapSignalElementCategory> DisplayOrder { get; } =
    [
        RouteMapSignalElementCategory.System,
        RouteMapSignalElementCategory.TopBar,
        RouteMapSignalElementCategory.Node,
        RouteMapSignalElementCategory.Segment,
        RouteMapSignalElementCategory.Card,
        RouteMapSignalElementCategory.Vehicle,
        RouteMapSignalElementCategory.Common
    ];

    private bool _isExpanded;

    internal RouteMapSignalMappingGroup(
        RouteMapSignalElementCategory category,
        IReadOnlyList<RouteMapSignalMappingRow> rows,
        bool isExpanded)
    {
        Category = category;
        Rows = rows;
        _isExpanded = isExpanded;
    }

    internal RouteMapSignalElementCategory Category { get; }
    public string Title => Category switch
    {
        RouteMapSignalElementCategory.System => "Системные",
        RouteMapSignalElementCategory.TopBar => "TopBar",
        RouteMapSignalElementCategory.Node => "Узлы",
        RouteMapSignalElementCategory.Segment => "Линии",
        RouteMapSignalElementCategory.Card => "Карточки",
        RouteMapSignalElementCategory.Vehicle => "Объекты",
        RouteMapSignalElementCategory.Common => "Общие",
        _ => Category.ToString()
    };

    public IReadOnlyList<RouteMapSignalMappingRow> Rows { get; }
    public int Count => Rows.Count;
    public string CountText => $"{Count} сигналов";

    public bool IsExpanded
    {
        get => _isExpanded;
        set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
    }
}
