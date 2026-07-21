using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using Configurator.Application.Services;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Workspace.RouteMap.SignalMapping;
using Microsoft.Extensions.Options;
using ReactiveUI;

namespace Configurator.Desktop.Workspace.ModbusDemo;

/// <summary>
/// Диагностический каталог активных Coils, Holding Registers и их битов.
/// </summary>
public sealed class ModbusAddressCatalogViewModel : ViewModelBase, IDisposable
{
    private const int MaxCoils = 2000;
    private const int MaxRegisters = 123;

    private readonly IOptionsMonitor<ModbusOptions>? _mapOptionsMonitor;
    private readonly RouteMapConfigurationManager? _routeMapConfigurationManager;
    private readonly IModbusRuntimeService? _runtimeService;
    private readonly IAppConfigService _appConfigService;
    private readonly Func<ModbusOptions> _runtimeOptionsProvider;
    private readonly Action<Action> _dispatchToUi;
    private readonly IDisposable? _optionsSubscription;
    private readonly IDisposable? _definitionSubscription;
    private ModbusOptions? _runtimeOptionsOverride;
    private ModbusOptions _catalogRuntimeOptions = new();
    private ModbusSnapshot _clientSnapshot = ModbusSnapshot.Empty;
    private ModbusSnapshot _serverSnapshot = ModbusSnapshot.Empty;
    private ModbusAddressCatalogRow? _editingRow;
    private bool _disposed;
    private string? _lastError;
    private string? _statusMessage;

    public ModbusAddressCatalogViewModel(
        IAppConfigService appConfigService,
        Func<ModbusOptions> runtimeOptionsProvider,
        Action<Action> dispatchToUi,
        IOptionsMonitor<ModbusOptions>? mapOptionsMonitor = null,
        RouteMapConfigurationManager? routeMapConfigurationManager = null,
        IModbusRuntimeService? runtimeService = null)
    {
        _appConfigService = appConfigService;
        _runtimeOptionsProvider = runtimeOptionsProvider;
        _dispatchToUi = dispatchToUi;
        _mapOptionsMonitor = mapOptionsMonitor;
        _routeMapConfigurationManager = routeMapConfigurationManager;
        _runtimeService = runtimeService;

        ToggleEditCommand = ReactiveCommand.CreateFromTask<ModbusAddressCatalogRow>(ToggleEditAsync);
        ExpandAllCommand = ReactiveCommand.Create(ExpandAll);
        CollapseAllCommand = ReactiveCommand.Create(CollapseAll);
        RebuildFromCurrent();

        if (_runtimeService is not null)
        {
            _clientSnapshot = _runtimeService.ClientSnapshot;
            _serverSnapshot = _runtimeService.ServerSnapshot;
            _runtimeService.SnapshotChanged += OnRuntimeSnapshotChanged;
            ApplyCurrentValues();
        }

        if (_mapOptionsMonitor is not null)
        {
            _optionsSubscription = _mapOptionsMonitor.OnChange((options, name) =>
                _dispatchToUi(() =>
                {
                    if (string.Equals(name, ModbusOptions.DemoSectionName, StringComparison.Ordinal))
                    {
                        _runtimeOptionsOverride = options.Clone();
                    }

                    RebuildFromCurrent();
                }));
        }

        if (_routeMapConfigurationManager is not null)
        {
            _definitionSubscription = _routeMapConfigurationManager.DefinitionChanges
                .Skip(1)
                .Subscribe(_ => _dispatchToUi(RebuildFromCurrent));
        }
    }

    public ObservableCollection<ModbusAddressCatalogRow> CoilRows { get; } = [];

    public ObservableCollection<ModbusAddressCatalogRow> HoldingRegisterRows { get; } = [];

    public ReactiveCommand<ModbusAddressCatalogRow, Unit> ToggleEditCommand { get; }

    public ReactiveCommand<Unit, Unit> ExpandAllCommand { get; }

    public ReactiveCommand<Unit, Unit> CollapseAllCommand { get; }

    public string? LastError
    {
        get => _lastError;
        private set => this.RaiseAndSetIfChanged(ref _lastError, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public event EventHandler<ModbusAddressCatalogMessageChangedEventArgs>? MessageChanged;

    public void Refresh(ModbusOptions? runtimeOptions = null)
    {
        _runtimeOptionsOverride = runtimeOptions?.Clone();
        RebuildFromCurrent();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _optionsSubscription?.Dispose();
        _definitionSubscription?.Dispose();
        if (_runtimeService is not null)
        {
            _runtimeService.SnapshotChanged -= OnRuntimeSnapshotChanged;
        }
    }

    private async Task ToggleEditAsync(ModbusAddressCatalogRow row)
    {
        if (_editingRow is not null && !ReferenceEquals(_editingRow, row))
        {
            if (!await SaveEditingRowAsync(_editingRow))
            {
                return;
            }
        }

        if (ReferenceEquals(_editingRow, row))
        {
            await SaveEditingRowAsync(row);
            return;
        }

        row.BeginEdit();
        _editingRow = row;
        ClearMessage();
    }

    private async Task<bool> SaveEditingRowAsync(ModbusAddressCatalogRow row)
    {
        var displayName = row.EditDisplayName.Trim();

        try
        {
            var latest = _mapOptionsMonitor?.CurrentValue.Clone()
                         ?? _appConfigService.GetSection<ModbusOptions>(ModbusOptions.SectionName);
            latest.AddressLabels ??= [];
            latest.AddressLabels.RemoveAll(label => IsSameCoordinate(label, row));
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                latest.AddressLabels.Add(new ModbusAddressLabelOptions
                {
                    Area = row.Area,
                    Address = row.Address,
                    BitIndex = row.BitIndex,
                    DisplayName = displayName
                });
            }

            await _appConfigService.SaveSectionAsync(ModbusOptions.SectionName, latest);
            row.SetSavedDisplayName(displayName);
            _editingRow = null;
            SetMessage("Диагностическое имя Modbus сохранено.", null);
            return true;
        }
        catch (Exception ex)
        {
            SetMessage(null, $"Не удалось сохранить диагностическое имя Modbus: {ex.Message}");
            return false;
        }
    }

    private void RebuildFromCurrent()
    {
        if (_disposed)
        {
            return;
        }

        _editingRow?.CancelEdit();
        _editingRow = null;

        var runtime = (_runtimeOptionsOverride ?? _runtimeOptionsProvider()).Clone();
        _catalogRuntimeOptions = runtime;
        var map = _mapOptionsMonitor?.CurrentValue.Clone()
                  ?? _appConfigService.GetSection<ModbusOptions>(ModbusOptions.SectionName);
        map.AddressLabels ??= [];

        var labels = map.AddressLabels
            .Where(IsValidLabel)
            .GroupBy(label => (label.Area, label.Address, label.BitIndex))
            .ToDictionary(group => group.Key, group => group.Last().DisplayName.Trim());
        var coilRows = CreateRows(
            ModbusDataArea.Coil,
            ActiveCoilCount(runtime),
            labels,
            address => FormatFullAddress(runtime, ModbusDataArea.Coil, address));
        var registerRows = CreateRows(
            ModbusDataArea.HoldingRegister,
            ActiveRegisterCount(runtime),
            labels,
            address => FormatFullAddress(runtime, ModbusDataArea.HoldingRegister, address));
        var rowsByCoordinate = coilRows
            .Concat(registerRows)
            .SelectMany(row => new[] { row }.Concat(row.Bits))
            .ToDictionary(row => (row.Area, row.Address, row.BitIndex));
        var routeSignals = _routeMapConfigurationManager is null
            ? new Dictionary<string, RouteMapSignalInventoryItem>(StringComparer.OrdinalIgnoreCase)
            : RouteMapSignalInventory.Build(_routeMapConfigurationManager.CurrentDefinition)
                .ToDictionary(item => item.SignalId, StringComparer.OrdinalIgnoreCase);

        AddRouteMapUsages(map.DataMap ?? [], routeSignals, rowsByCoordinate);
        AddAlarmUsages(map.AlarmMap ?? [], rowsByCoordinate);

        foreach (var row in rowsByCoordinate.Values)
        {
            row.CompleteUsages();
        }

        ReplaceRows(CoilRows, coilRows);
        ReplaceRows(HoldingRegisterRows, registerRows);
        ApplyCurrentValues();
    }

    private void OnRuntimeSnapshotChanged(object? sender, ModbusSnapshot snapshot) =>
        _dispatchToUi(() =>
        {
            if (_disposed)
            {
                return;
            }

            switch (snapshot.Role)
            {
                case ModbusRuntimeRole.Client:
                    _clientSnapshot = snapshot;
                    break;
                case ModbusRuntimeRole.Server:
                    _serverSnapshot = snapshot;
                    break;
                default:
                    return;
            }

            ApplyCurrentValues();
        });

    private void ApplyCurrentValues()
    {
        foreach (var row in CoilRows)
        {
            row.SetCurrentValue(FormatCurrentValue(row));
        }

        foreach (var row in HoldingRegisterRows)
        {
            row.SetCurrentValue(FormatCurrentValue(row));
            foreach (var bit in row.Bits)
            {
                bit.SetCurrentValue(FormatCurrentValue(bit));
            }
        }
    }

    private string FormatCurrentValue(ModbusAddressCatalogRow row)
    {
        var values = new List<(string Role, string Value)>();
        AddValue("Client", _catalogRuntimeOptions.Client, _clientSnapshot);
        AddValue("Server", _catalogRuntimeOptions.Server, _serverSnapshot);

        if (values.Count == 0)
        {
            return "—";
        }

        var distinctValues = values
            .Select(item => item.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (distinctValues.Length == 1)
        {
            return distinctValues[0];
        }

        return string.Join(" / ", values.Select(item => $"{item.Role}: {item.Value}"));

        void AddValue(string role, ModbusEndpointOptions endpoint, ModbusSnapshot snapshot)
        {
            if (!IsAddressInActiveEndpoint(endpoint, row.Area, row.Address)
                || !TryGetSnapshotValue(snapshot, row.Area, row.Address, row.BitIndex, out var value))
            {
                return;
            }

            values.Add((role, value));
        }
    }

    private static bool IsAddressInActiveEndpoint(
        ModbusEndpointOptions endpoint,
        ModbusDataArea area,
        int address)
    {
        var isAreaEnabled = area == ModbusDataArea.Coil
            ? endpoint.CoilsEnabled
            : endpoint.HoldingRegistersEnabled;
        var count = area == ModbusDataArea.Coil ? endpoint.CoilCount : endpoint.RegisterCount;
        return endpoint.Enabled && isAreaEnabled && address >= 0 && address < count;
    }

    private static bool TryGetSnapshotValue(
        ModbusSnapshot snapshot,
        ModbusDataArea area,
        int address,
        int? bitIndex,
        out string value)
    {
        if (area == ModbusDataArea.Coil)
        {
            if (address >= 0 && address < snapshot.Coils.Count)
            {
                value = snapshot.Coils[address] ? "1" : "0";
                return true;
            }

            value = string.Empty;
            return false;
        }

        if (address < 0 || address >= snapshot.HoldingRegisters.Count)
        {
            value = string.Empty;
            return false;
        }

        var registerValue = snapshot.HoldingRegisters[address];
        value = bitIndex is int bit
            ? ((registerValue & (1 << bit)) != 0 ? "1" : "0")
            : registerValue.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static List<ModbusAddressCatalogRow> CreateRows(
        ModbusDataArea area,
        int count,
        IReadOnlyDictionary<(ModbusDataArea Area, int Address, int? BitIndex), string> labels,
        Func<int, string> fullAddressFormatter)
    {
        var rows = new List<ModbusAddressCatalogRow>(count);
        for (var address = 0; address < count; address++)
        {
            labels.TryGetValue((area, address, null), out var name);
            var fullAddress = fullAddressFormatter(address);
            var row = new ModbusAddressCatalogRow(area, address, null, name ?? string.Empty, fullAddress);
            if (area == ModbusDataArea.HoldingRegister)
            {
                for (var bit = 0; bit < 16; bit++)
                {
                    labels.TryGetValue((area, address, bit), out var bitName);
                    row.Bits.Add(new ModbusAddressCatalogRow(area, address, bit, bitName ?? string.Empty, fullAddress));
                }
            }

            rows.Add(row);
        }

        return rows;
    }

    private static void AddRouteMapUsages(
        IEnumerable<ModbusDataPointOptions> points,
        IReadOnlyDictionary<string, RouteMapSignalInventoryItem> routeSignals,
        IReadOnlyDictionary<(ModbusDataArea Area, int Address, int? BitIndex), ModbusAddressCatalogRow> rows)
    {
        foreach (var point in points)
        {
            if (!routeSignals.TryGetValue(point.Name, out var signal))
            {
                continue;
            }

            if (point.Area == ModbusDataArea.HoldingRegister && point.BitIndex is int bitIndex)
            {
                AddUsage(rows, point.Area, point.Address, bitIndex, "RouteMap", signal.SignalId, signal.Roles);
                AddUsage(rows, point.Area, point.Address, null, "RouteMap", signal.SignalId, $"{signal.Roles} (бит {bitIndex})");
                continue;
            }

            for (var address = point.Address; address < point.Address + point.Length; address++)
            {
                AddUsage(
                    rows,
                    point.Area,
                    address,
                    null,
                    "RouteMap",
                    signal.SignalId,
                    signal.Roles,
                    point.Area == ModbusDataArea.HoldingRegister ? signal.SignalId : null);
            }
        }
    }

    private static void AddAlarmUsages(
        IEnumerable<ModbusAlarmOptions> alarms,
        IReadOnlyDictionary<(ModbusDataArea Area, int Address, int? BitIndex), ModbusAddressCatalogRow> rows)
    {
        foreach (var alarm in alarms)
        {
            AddAlarmUsage(alarm.Alarm, "Alarm", alarm);
            if (alarm.AcknowledgementEnabled)
            {
                AddAlarmUsage(alarm.Acknowledgement, "OK", alarm);
            }

            if (alarm.RegisterValueEnabled)
            {
                var state = alarm.Enabled ? string.Empty : " (выкл.)";
                AddUsage(
                    rows,
                    ModbusDataArea.HoldingRegister,
                    alarm.RegisterValueAddress,
                    null,
                    "Менеджер тревог",
                    string.Empty,
                    $"Значение диалога: {alarm.Id}{state}");
            }
        }

        void AddAlarmUsage(ModbusBitAddressOptions bitAddress, string role, ModbusAlarmOptions alarm)
        {
            var state = alarm.Enabled ? string.Empty : " (выкл.)";
            var text = $"{role}: {alarm.Id}{state}";
            if (bitAddress.Area == ModbusDataArea.HoldingRegister)
            {
                if (bitAddress.BitIndex is not int bitIndex)
                {
                    return;
                }

                AddUsage(rows, bitAddress.Area, bitAddress.Address, bitIndex, "Менеджер тревог", string.Empty, text);
                AddUsage(rows, bitAddress.Area, bitAddress.Address, null, "Менеджер тревог", string.Empty, $"{text} (бит {bitIndex})");
                return;
            }

            AddUsage(rows, bitAddress.Area, bitAddress.Address, null, "Менеджер тревог", string.Empty, text);
        }
    }

    private static void AddUsage(
        IReadOnlyDictionary<(ModbusDataArea Area, int Address, int? BitIndex), ModbusAddressCatalogRow> rows,
        ModbusDataArea area,
        int address,
        int? bitIndex,
        string source,
        string signalId,
        string role,
        string? descriptionSignalId = null)
    {
        if (rows.TryGetValue((area, address, bitIndex), out var row))
        {
            row.AddUsage(source, signalId, role, descriptionSignalId);
        }
    }

    private static int ActiveCoilCount(ModbusOptions runtime) =>
        ActiveEndpoints(runtime, endpoint => endpoint.CoilsEnabled, endpoint => endpoint.CoilCount, MaxCoils);

    private static int ActiveRegisterCount(ModbusOptions runtime) =>
        ActiveEndpoints(runtime, endpoint => endpoint.HoldingRegistersEnabled, endpoint => endpoint.RegisterCount, MaxRegisters);

    private static int ActiveEndpoints(
        ModbusOptions runtime,
        Func<ModbusEndpointOptions, bool> isAreaEnabled,
        Func<ModbusEndpointOptions, int> countSelector,
        int maximum)
    {
        return Math.Clamp(new[] { runtime.Client, runtime.Server }
            .Where(endpoint => endpoint.Enabled && isAreaEnabled(endpoint))
            .Select(countSelector)
            .DefaultIfEmpty(0)
            .Max(), 0, maximum);
    }

    private static bool IsValidLabel(ModbusAddressLabelOptions label) =>
        !string.IsNullOrWhiteSpace(label.DisplayName)
        && Enum.IsDefined(label.Area)
        && label.Address >= 0
        && label.Address < (label.Area == ModbusDataArea.Coil ? MaxCoils : MaxRegisters)
        && (label.Area != ModbusDataArea.Coil || label.BitIndex is null)
        && (label.BitIndex is null || label.BitIndex is >= 0 and <= 15);

    private static bool IsSameCoordinate(ModbusAddressLabelOptions label, ModbusAddressCatalogRow row) =>
        label.Area == row.Area && label.Address == row.Address && label.BitIndex == row.BitIndex;

    private void ExpandAll()
    {
        foreach (var row in HoldingRegisterRows)
        {
            row.SetExpanded(true);
        }
    }

    private void CollapseAll()
    {
        foreach (var row in HoldingRegisterRows)
        {
            row.SetExpanded(false);
        }
    }

    private static string FormatFullAddress(ModbusOptions runtime, ModbusDataArea area, int offset)
    {
        var addresses = new List<(string Role, int Address)>();
        AddAddress("Client", runtime.Client);
        AddAddress("Server", runtime.Server);

        if (addresses.Count == 0)
        {
            return "—";
        }

        var distinctAddresses = addresses.Select(item => item.Address).Distinct().ToArray();
        if (distinctAddresses.Length == 1)
        {
            return distinctAddresses[0].ToString(CultureInfo.InvariantCulture);
        }

        return string.Join(
            " / ",
            addresses.Select(item => $"{item.Role}: {item.Address.ToString(CultureInfo.InvariantCulture)}"));

        void AddAddress(string role, ModbusEndpointOptions endpoint)
        {
            var isAreaEnabled = area == ModbusDataArea.Coil
                ? endpoint.CoilsEnabled
                : endpoint.HoldingRegistersEnabled;
            var count = area == ModbusDataArea.Coil ? endpoint.CoilCount : endpoint.RegisterCount;
            var startAddress = area == ModbusDataArea.Coil
                ? endpoint.CoilStartAddress
                : endpoint.HoldingRegisterStartAddress;
            if (endpoint.Enabled && isAreaEnabled && offset >= 0 && offset < count)
            {
                addresses.Add((role, startAddress + offset));
            }
        }
    }

    private static void ReplaceRows(
        ObservableCollection<ModbusAddressCatalogRow> target,
        IEnumerable<ModbusAddressCatalogRow> source)
    {
        target.Clear();
        foreach (var row in source)
        {
            target.Add(row);
        }
    }

    private void ClearMessage()
    {
        LastError = null;
        StatusMessage = null;
        MessageChanged?.Invoke(this, new ModbusAddressCatalogMessageChangedEventArgs(null, null));
    }

    private void SetMessage(string? statusMessage, string? errorMessage)
    {
        StatusMessage = statusMessage;
        LastError = errorMessage;
        MessageChanged?.Invoke(this, new ModbusAddressCatalogMessageChangedEventArgs(statusMessage, errorMessage));
    }
}

public sealed class ModbusAddressCatalogRow : ViewModelBase
{
    private readonly List<ModbusAddressCatalogUsage> _usages = [];
    private readonly List<string> _descriptionSignalIds = [];
    private string _displayName;
    private string _editDisplayName;
    private bool _isEditing;
    private string _usageText = "Не используется";
    private string _signalIdText = "—";
    private string _roleText = "—";
    private string _descriptionText = "—";
    private string _currentValueText = "—";
    private bool _isExpanded;

    internal ModbusAddressCatalogRow(
        ModbusDataArea area,
        int address,
        int? bitIndex,
        string displayName,
        string fullAddressText)
    {
        Area = area;
        Address = address;
        BitIndex = bitIndex;
        _displayName = displayName;
        _editDisplayName = displayName;
        FullAddressText = fullAddressText;
    }

    public ModbusDataArea Area { get; }

    public int Address { get; }

    public int? BitIndex { get; }

    public string AddressText => BitIndex is int bitIndex ? $"Бит {bitIndex}" : Address.ToString();

    public string FullAddressText { get; }

    /// <summary>
    /// Последнее доступное значение из runtime Modbus. Для разных Client и Server показывает оба значения.
    /// </summary>
    public string CurrentValueText
    {
        get => _currentValueText;
        private set => this.RaiseAndSetIfChanged(ref _currentValueText, value);
    }

    public ObservableCollection<ModbusAddressCatalogRow> Bits { get; } = [];

    public string DisplayName
    {
        get => _displayName;
        private set
        {
            this.RaiseAndSetIfChanged(ref _displayName, value);
            this.RaisePropertyChanged(nameof(DisplayNameText));
        }
    }

    public string DisplayNameText => string.IsNullOrWhiteSpace(DisplayName) ? "Без имени" : DisplayName;

    public string EditDisplayName
    {
        get => _editDisplayName;
        set => this.RaiseAndSetIfChanged(ref _editDisplayName, value);
    }

    public bool IsEditing
    {
        get => _isEditing;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isEditing, value);
            this.RaisePropertyChanged(nameof(IsNotEditing));
        }
    }

    public bool IsNotEditing => !IsEditing;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
    }

    public string UsageText
    {
        get => _usageText;
        private set => this.RaiseAndSetIfChanged(ref _usageText, value);
    }

    public string SignalIdText
    {
        get => _signalIdText;
        private set => this.RaiseAndSetIfChanged(ref _signalIdText, value);
    }

    public string RoleText
    {
        get => _roleText;
        private set => this.RaiseAndSetIfChanged(ref _roleText, value);
    }

    public string DescriptionText
    {
        get => _descriptionText;
        private set => this.RaiseAndSetIfChanged(ref _descriptionText, value);
    }

    internal void BeginEdit()
    {
        EditDisplayName = DisplayName;
        IsEditing = true;
    }

    internal void CancelEdit()
    {
        EditDisplayName = DisplayName;
        IsEditing = false;
    }

    internal void SetSavedDisplayName(string displayName)
    {
        DisplayName = displayName;
        EditDisplayName = displayName;
        IsEditing = false;
    }

    internal void SetExpanded(bool isExpanded) => IsExpanded = isExpanded;

    internal void SetCurrentValue(string currentValue) => CurrentValueText = currentValue;

    internal void AddUsage(string source, string signalId, string role, string? descriptionSignalId = null)
    {
        var usage = new ModbusAddressCatalogUsage(source, signalId, role);
        if (!_usages.Contains(usage))
        {
            _usages.Add(usage);
        }

        if (Area == ModbusDataArea.HoldingRegister
            && BitIndex is null
            && !string.IsNullOrWhiteSpace(descriptionSignalId))
        {
            _descriptionSignalIds.Add(descriptionSignalId);
        }
    }

    internal void CompleteUsages()
    {
        UsageText = Area == ModbusDataArea.HoldingRegister && BitIndex is null
            ? _usages.Count == 0 ? "Нет" : "Да"
            : _usages.Count == 0
                ? "Не используется"
                : string.Join(", ", _usages.Select(usage => usage.Source).Distinct());
        SignalIdText = _usages
            .Where(usage => !string.IsNullOrWhiteSpace(usage.SignalId))
            .Select(usage => usage.SignalId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .DefaultIfEmpty("—")
            .Aggregate((left, right) => $"{left}; {right}");
        RoleText = _usages
            .Select(usage => usage.Role)
            .Distinct(StringComparer.Ordinal)
            .DefaultIfEmpty("—")
            .Aggregate((left, right) => $"{left}; {right}");
        DescriptionText = Area == ModbusDataArea.HoldingRegister && BitIndex is null
            ? _descriptionSignalIds
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .DefaultIfEmpty("—")
                .Aggregate((left, right) => $"{left}; {right}")
            : "—";
    }
}

public sealed record ModbusAddressCatalogMessageChangedEventArgs(string? StatusMessage, string? ErrorMessage);

internal sealed record ModbusAddressCatalogUsage(string Source, string SignalId, string Role);
