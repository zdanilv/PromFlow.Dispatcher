using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reactive;
using Configurator.Application.Services;
using Configurator.Application.Services.Dialogs;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Desktop.Workspace.ModbusProfile;
using Microsoft.Extensions.Options;
using ReactiveUI;

namespace Configurator.Desktop.Workspace.Alarms;

public sealed class AlarmManagerViewModel : ViewModelBase, IDisposable
{
    private readonly IOptionsMonitor<ModbusOptions> _optionsMonitor;
    private readonly IAppConfigService _appConfigService;
    private readonly IModbusAlarmMapValidator _validator;
    private readonly IModbusTcpProfileFilePicker? _profileFilePicker;
    private readonly IModbusTcpProfileTransferService? _profileTransferService;
    private readonly IDialogService? _dialogService;
    private readonly IDisposable? _optionsSubscription;
    private ModbusOptions _baseOptions;
    private ModbusOptions _addressOptions;
    private bool _isLoading;
    private bool _isDirty;
    private bool _hasExternalChanges;
    private string? _errorMessage;
    private string? _statusMessage;
    private bool _disposed;

    public AlarmManagerViewModel(
        IOptionsMonitor<ModbusOptions> optionsMonitor,
        IAppConfigService appConfigService,
        IModbusAlarmMapValidator validator,
        IModbusTcpProfileFilePicker? profileFilePicker = null,
        IModbusTcpProfileTransferService? profileTransferService = null,
        IDialogService? dialogService = null)
    {
        _optionsMonitor = optionsMonitor;
        _appConfigService = appConfigService;
        _validator = validator;
        _profileFilePicker = profileFilePicker;
        _profileTransferService = profileTransferService;
        _dialogService = dialogService;
        _baseOptions = optionsMonitor.CurrentValue.Clone();
        _addressOptions = ReadAddressOptions();

        AddAlarmCommand = ReactiveCommand.Create(AddAlarm);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        ReloadCommand = ReactiveCommand.Create(Reload);
        DuplicateAlarmCommand = ReactiveCommand.Create<AlarmManagerRow>(DuplicateAlarm);
        RemoveAlarmCommand = ReactiveCommand.Create<AlarmManagerRow>(RemoveAlarm);
        ImportProfileCommand = ReactiveCommand.CreateFromTask(ImportProfileAsync);
        ExportProfileCommand = ReactiveCommand.CreateFromTask(ExportProfileAsync);

        RebuildRows(_baseOptions);
        _optionsSubscription = optionsMonitor.OnChange((options, name) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => HandleExternalOptions(options, name));
        });
        if (_profileTransferService is not null)
        {
            _profileTransferService.ProfileApplied += OnProfileApplied;
        }
    }

    public ObservableCollection<AlarmManagerRow> Rows { get; } = [];
    public IReadOnlyList<ModbusAlarmKind> AlarmKinds { get; } = Enum.GetValues<ModbusAlarmKind>();
    public IReadOnlyList<ModbusDataArea> DataAreas { get; } =
    [
        ModbusDataArea.Coil,
        ModbusDataArea.HoldingRegister
    ];
    public ReactiveCommand<Unit, Unit> AddAlarmCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> ReloadCommand { get; }
    public ReactiveCommand<AlarmManagerRow, Unit> DuplicateAlarmCommand { get; }
    public ReactiveCommand<AlarmManagerRow, Unit> RemoveAlarmCommand { get; }
    public ReactiveCommand<Unit, Unit> ImportProfileCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportProfileCommand { get; }

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
        _optionsSubscription?.Dispose();
        if (_profileTransferService is not null)
        {
            _profileTransferService.ProfileApplied -= OnProfileApplied;
        }
        DetachRows();
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
                $"{row.Id}: {row.ValidationMessage}"));
            return;
        }

        var latest = _optionsMonitor.CurrentValue.Clone();
        latest.AlarmMap = Rows.Select(row => row.ToOptions()).ToList();
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
            StatusMessage = "Менеджер тревог сохранен в Modbus.AlarmMap.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Не удалось сохранить Менеджер тревог: {ex.Message}";
        }
    }

    private void AddAlarm()
    {
        var options = new ModbusAlarmOptions
        {
            Id = NextId(),
            Kind = ModbusAlarmKind.Fault,
            Message = "Новое сообщение тревоги",
            Alarm = new ModbusBitAddressOptions
            {
                Area = ModbusDataArea.Coil,
                Address = 0
            },
            Acknowledgement = new ModbusBitAddressOptions
            {
                Area = ModbusDataArea.Coil,
                Address = 1
            }
        };
        var row = new AlarmManagerRow(options);
        AttachRow(row);
        Rows.Add(row);
        MarkDirty(row);
    }

    private void DuplicateAlarm(AlarmManagerRow row)
    {
        var copy = row.ToOptions();
        copy.Id = NextId(row.Id);
        var duplicate = new AlarmManagerRow(copy);
        AttachRow(duplicate);
        Rows.Add(duplicate);
        MarkDirty(duplicate);
    }

    private void RemoveAlarm(AlarmManagerRow row)
    {
        row.PropertyChanged -= OnRowPropertyChanged;
        Rows.Remove(row);
        IsDirty = true;
        StatusMessage = null;
    }

    private void Reload()
    {
        RebuildRows(_optionsMonitor.CurrentValue.Clone());
        ErrorMessage = null;
        StatusMessage = "Modbus.AlarmMap перечитан из конфигурации.";
        HasExternalChanges = false;
    }

    private async Task ImportProfileAsync()
    {
        if (_profileFilePicker is null || _profileTransferService is null || _dialogService is null)
        {
            ErrorMessage = "Импорт профиля Modbus TCP недоступен.";
            return;
        }

        var path = await _profileFilePicker.PickImportPathAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var preview = await _profileTransferService.ReadAndValidateAsync(path);
        if (!preview.Succeeded || preview.Profile is null)
        {
            ErrorMessage = preview.ErrorMessage ?? "Профиль Modbus TCP не прошёл проверку.";
            return;
        }

        var message = preview.RangeCorrection.HasChanges
            ? preview.RangeCorrection.ToConfirmationMessage()
            : "Импорт заменит настройки Modbus TCP, связи SignalId и Менеджер тревог. Продолжить?";
        if (IsDirty)
        {
            message = "Несохранённый черновик тревог будет заменён.\n\n" + message;
        }

        if (!await _dialogService.ConfirmAsync(message))
        {
            return;
        }

        var result = await _profileTransferService.ApplyAsync(preview.Profile, preview.RangeCorrection.HasChanges);
        if (!result.Succeeded)
        {
            ErrorMessage = result.ErrorMessage ?? "Не удалось применить профиль Modbus TCP.";
            return;
        }

        ErrorMessage = null;
        StatusMessage = result.WarningMessage ?? "Профиль Modbus TCP импортирован.";
    }

    private async Task ExportProfileAsync()
    {
        if (_profileFilePicker is null || _profileTransferService is null)
        {
            ErrorMessage = "Экспорт профиля Modbus TCP недоступен.";
            return;
        }

        var path = await _profileFilePicker.PickExportPathAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            await _profileTransferService.ExportAsync(path);
            ErrorMessage = null;
            StatusMessage = $"Профиль Modbus TCP экспортирован: {path}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Не удалось экспортировать профиль Modbus TCP: {ex.Message}";
        }
    }

    private void OnProfileApplied(object? sender, EventArgs args)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            RebuildRows(_optionsMonitor.CurrentValue.Clone());
            HasExternalChanges = false;
        });
    }

    private void HandleExternalOptions(ModbusOptions options, string? name)
    {
        if (string.Equals(name, ModbusOptions.DemoSectionName, StringComparison.Ordinal))
        {
            RefreshPhysicalAddresses();
            return;
        }

        if (AlarmMapsEquivalent(options.AlarmMap, _baseOptions.AlarmMap))
        {
            return;
        }

        if (IsDirty)
        {
            HasExternalChanges = true;
            StatusMessage = "Modbus.AlarmMap изменен извне. Нажмите ПЕРЕЗАГРУЗИТЬ, чтобы отказаться от локального черновика.";
            return;
        }

        RebuildRows(options.Clone());
        StatusMessage = "Modbus.AlarmMap обновлен из конфигурации.";
    }

    private void RebuildRows(ModbusOptions options)
    {
        _isLoading = true;
        try
        {
            DetachRows();
            Rows.Clear();
            _baseOptions = options.Clone();
            _addressOptions = ReadAddressOptions();
            foreach (var alarm in options.AlarmMap)
            {
                var row = new AlarmManagerRow(alarm);
                AttachRow(row);
                Rows.Add(row);
                ValidateRow(row);
            }

            IsDirty = false;
            HasExternalChanges = false;
            this.RaisePropertyChanged(nameof(ClientAddressBase));
            this.RaisePropertyChanged(nameof(ServerAddressBase));
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void AttachRow(AlarmManagerRow row)
    {
        row.UpdateAddressBases(_addressOptions.Client, _addressOptions.Server);
        row.PropertyChanged += OnRowPropertyChanged;
        ValidateRow(row);
    }

    private void DetachRows()
    {
        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (_isLoading
            || sender is not AlarmManagerRow row
            || eventArgs.PropertyName is not (
                nameof(AlarmManagerRow.Enabled)
                or nameof(AlarmManagerRow.Id)
                or nameof(AlarmManagerRow.Kind)
                or nameof(AlarmManagerRow.Message)
                or nameof(AlarmManagerRow.AlarmArea)
                or nameof(AlarmManagerRow.AlarmAddress)
                or nameof(AlarmManagerRow.AlarmBitIndex)
                or nameof(AlarmManagerRow.AcknowledgementArea)
                or nameof(AlarmManagerRow.AcknowledgementAddress)
                or nameof(AlarmManagerRow.AcknowledgementBitIndex)
                or nameof(AlarmManagerRow.RepeatIntervalMs)
                or nameof(AlarmManagerRow.AcknowledgementPulseDurationMs)
                or nameof(AlarmManagerRow.PhysicalAddressError)))
        {
            return;
        }

        MarkDirty(row);
    }

    private void MarkDirty(AlarmManagerRow row)
    {
        IsDirty = true;
        StatusMessage = null;
        row.UpdateAddressBases(_addressOptions.Client, _addressOptions.Server);
        ValidateRow(row);
    }

    private void ValidateRow(AlarmManagerRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.PhysicalAddressError))
        {
            row.SetValidation(row.PhysicalAddressError);
            return;
        }

        var options = CreateValidationOptions(_baseOptions);
        options.AlarmMap = [row.ToOptions()];
        var validation = ValidateOptions(options);
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

    private void RefreshPhysicalAddresses()
    {
        _addressOptions = ReadAddressOptions();
        foreach (var row in Rows)
        {
            row.UpdateAddressBases(_addressOptions.Client, _addressOptions.Server);
            ValidateRow(row);
        }

        this.RaisePropertyChanged(nameof(ClientAddressBase));
        this.RaisePropertyChanged(nameof(ServerAddressBase));
    }

    private ModbusOptions ReadAddressOptions()
        => _optionsMonitor.Get(ModbusOptions.DemoSectionName).Clone();

    private string NextId(string seed = "alarm")
    {
        var baseId = string.IsNullOrWhiteSpace(seed) ? "alarm" : seed.Trim();
        var used = Rows.Select(row => row.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(baseId))
        {
            return baseId;
        }

        for (var index = 1; index < 10000; index++)
        {
            var candidate = $"{baseId}.{index}";
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{baseId}.{DateTimeOffset.Now.ToUnixTimeMilliseconds()}";
    }

    private static bool AlarmMapsEquivalent(
        IReadOnlyList<ModbusAlarmOptions> left,
        IReadOnlyList<ModbusAlarmOptions> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        return left.Zip(right).All(pair => AlarmEquivalent(pair.First, pair.Second));
    }

    private static bool AlarmEquivalent(ModbusAlarmOptions left, ModbusAlarmOptions right)
        => left.Id == right.Id
           && left.Enabled == right.Enabled
           && left.Kind == right.Kind
           && left.Message == right.Message
           && AddressEquivalent(left.Alarm, right.Alarm)
           && AddressEquivalent(left.Acknowledgement, right.Acknowledgement)
           && left.RepeatIntervalMs == right.RepeatIntervalMs
           && left.AcknowledgementPulseDurationMs == right.AcknowledgementPulseDurationMs;

    private static bool AddressEquivalent(ModbusBitAddressOptions left, ModbusBitAddressOptions right)
        => left.Area == right.Area
           && left.Address == right.Address
           && left.BitIndex == right.BitIndex;

    private static string OperationMessage(ModbusOperationResult result)
        => result.ErrorDetails is { Length: > 0 }
            ? $"{result.ErrorMessage} {result.ErrorDetails}"
            : result.ErrorMessage ?? "Ошибка Modbus.AlarmMap.";
}

public sealed class AlarmManagerRow : ReactiveObject
{
    private bool _enabled;
    private string _id = string.Empty;
    private ModbusAlarmKind _kind;
    private string _message = string.Empty;
    private ModbusDataArea _alarmArea;
    private int _alarmAddress;
    private int? _alarmBitIndex;
    private ModbusDataArea _acknowledgementArea;
    private int _acknowledgementAddress;
    private int? _acknowledgementBitIndex;
    private int _repeatIntervalMs;
    private int _acknowledgementPulseDurationMs;
    private string? _validationMessage;
    private string? _physicalAddressError;
    private ModbusEndpointOptions _clientEndpoint = new();
    private ModbusEndpointOptions _serverEndpoint = new();

    public AlarmManagerRow(ModbusAlarmOptions options)
    {
        _enabled = options.Enabled;
        _id = options.Id;
        _kind = options.Kind;
        _message = options.Message;
        ApplyAlarmAddress(options.Alarm);
        ApplyAcknowledgementAddress(options.Acknowledgement);
        _repeatIntervalMs = options.RepeatIntervalMs;
        _acknowledgementPulseDurationMs = options.AcknowledgementPulseDurationMs;
    }

    public bool Enabled { get => _enabled; set => this.RaiseAndSetIfChanged(ref _enabled, value); }
    public string Id { get => _id; set => this.RaiseAndSetIfChanged(ref _id, value); }
    public ModbusAlarmKind Kind { get => _kind; set => this.RaiseAndSetIfChanged(ref _kind, value); }
    public string Message { get => _message; set => this.RaiseAndSetIfChanged(ref _message, value); }

    public ModbusDataArea AlarmArea
    {
        get => _alarmArea;
        set
        {
            if (_alarmArea == value)
            {
                return;
            }

            ClearPhysicalAddressError();
            this.RaiseAndSetIfChanged(ref _alarmArea, value);
            NormalizeAlarmBitIndex();
            RaiseAlarmAddressState();
        }
    }

    public int AlarmAddress
    {
        get => _alarmAddress;
        set
        {
            if (_alarmAddress == value)
            {
                return;
            }

            ClearPhysicalAddressError();
            this.RaiseAndSetIfChanged(ref _alarmAddress, value);
            RaiseAlarmPhysicalAddressText();
        }
    }

    public int? AlarmBitIndex
    {
        get => _alarmBitIndex;
        set => SetAlarmBitIndex(AlarmUsesRegisterBit ? value ?? 0 : null);
    }

    public ModbusDataArea AcknowledgementArea
    {
        get => _acknowledgementArea;
        set
        {
            if (_acknowledgementArea == value)
            {
                return;
            }

            ClearPhysicalAddressError();
            this.RaiseAndSetIfChanged(ref _acknowledgementArea, value);
            NormalizeAcknowledgementBitIndex();
            RaiseAcknowledgementAddressState();
        }
    }

    public int AcknowledgementAddress
    {
        get => _acknowledgementAddress;
        set
        {
            if (_acknowledgementAddress == value)
            {
                return;
            }

            ClearPhysicalAddressError();
            this.RaiseAndSetIfChanged(ref _acknowledgementAddress, value);
            RaiseAcknowledgementPhysicalAddressText();
        }
    }

    public int? AcknowledgementBitIndex
    {
        get => _acknowledgementBitIndex;
        set => SetAcknowledgementBitIndex(AcknowledgementUsesRegisterBit ? value ?? 0 : null);
    }

    public int RepeatIntervalMs { get => _repeatIntervalMs; set => this.RaiseAndSetIfChanged(ref _repeatIntervalMs, value); }
    public int AcknowledgementPulseDurationMs { get => _acknowledgementPulseDurationMs; set => this.RaiseAndSetIfChanged(ref _acknowledgementPulseDurationMs, value); }
    public string? ValidationMessage => _validationMessage;
    public bool HasError => !string.IsNullOrWhiteSpace(ValidationMessage);
    public string? PhysicalAddressError => _physicalAddressError;
    public bool CanEditAlarmBitIndex => AlarmUsesRegisterBit;
    public bool CanEditAcknowledgementBitIndex => AcknowledgementUsesRegisterBit;

    public string AlarmClientPhysicalAddressText
    {
        get => FormatPhysicalAddress(_clientEndpoint, AlarmArea, AlarmAddress);
        set => ApplyPhysicalAddress(value, _clientEndpoint, isAlarm: true);
    }

    public string AlarmServerPhysicalAddressText
    {
        get => FormatPhysicalAddress(_serverEndpoint, AlarmArea, AlarmAddress);
        set => ApplyPhysicalAddress(value, _serverEndpoint, isAlarm: true);
    }

    public string AcknowledgementClientPhysicalAddressText
    {
        get => FormatPhysicalAddress(_clientEndpoint, AcknowledgementArea, AcknowledgementAddress);
        set => ApplyPhysicalAddress(value, _clientEndpoint, isAlarm: false);
    }

    public string AcknowledgementServerPhysicalAddressText
    {
        get => FormatPhysicalAddress(_serverEndpoint, AcknowledgementArea, AcknowledgementAddress);
        set => ApplyPhysicalAddress(value, _serverEndpoint, isAlarm: false);
    }

    public ModbusAlarmOptions ToOptions()
        => new()
        {
            Id = Id,
            Enabled = Enabled,
            Kind = Kind,
            Message = Message,
            Alarm = new ModbusBitAddressOptions
            {
                Area = AlarmArea,
                Address = AlarmAddress,
                BitIndex = AlarmUsesRegisterBit ? AlarmBitIndex : null
            },
            Acknowledgement = new ModbusBitAddressOptions
            {
                Area = AcknowledgementArea,
                Address = AcknowledgementAddress,
                BitIndex = AcknowledgementUsesRegisterBit ? AcknowledgementBitIndex : null
            },
            RepeatIntervalMs = RepeatIntervalMs,
            AcknowledgementPulseDurationMs = AcknowledgementPulseDurationMs
        };

    public void SetValidation(string? message)
    {
        this.RaiseAndSetIfChanged(ref _validationMessage, message, nameof(ValidationMessage));
        this.RaisePropertyChanged(nameof(HasError));
    }

    public void UpdateAddressBases(ModbusEndpointOptions client, ModbusEndpointOptions server)
    {
        _clientEndpoint = client.Clone();
        _serverEndpoint = server.Clone();
        RaiseAlarmPhysicalAddressText();
        RaiseAcknowledgementPhysicalAddressText();
    }

    private bool AlarmUsesRegisterBit => AlarmArea == ModbusDataArea.HoldingRegister;
    private bool AcknowledgementUsesRegisterBit => AcknowledgementArea == ModbusDataArea.HoldingRegister;

    private void ApplyAlarmAddress(ModbusBitAddressOptions address)
    {
        _alarmArea = address.Area;
        _alarmAddress = address.Address;
        _alarmBitIndex = address.Area == ModbusDataArea.HoldingRegister ? address.BitIndex ?? 0 : null;
    }

    private void ApplyAcknowledgementAddress(ModbusBitAddressOptions address)
    {
        _acknowledgementArea = address.Area;
        _acknowledgementAddress = address.Address;
        _acknowledgementBitIndex = address.Area == ModbusDataArea.HoldingRegister ? address.BitIndex ?? 0 : null;
    }

    private void ApplyPhysicalAddress(
        string? value,
        ModbusEndpointOptions endpoint,
        bool isAlarm)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var physical))
        {
            SetPhysicalAddressError($"Физический адрес '{value}' должен быть целым числом.");
            return;
        }

        var currentArea = isAlarm ? AlarmArea : AcknowledgementArea;
        if (ResolveArea(physical, endpoint, currentArea) is not { } area)
        {
            SetPhysicalAddressError(
                $"Физический адрес {physical} не входит в диапазоны endpoint: " +
                $"Coil {FormatRange(endpoint.CoilStartAddress, endpoint.CoilCount)}, " +
                $"Holding Register {FormatRange(endpoint.HoldingRegisterStartAddress, endpoint.RegisterCount)}.");
            return;
        }

        ClearPhysicalAddressError();
        if (isAlarm)
        {
            AlarmArea = area;
            AlarmAddress = physical - BaseAddress(endpoint, area);
        }
        else
        {
            AcknowledgementArea = area;
            AcknowledgementAddress = physical - BaseAddress(endpoint, area);
        }
    }

    private static string FormatPhysicalAddress(
        ModbusEndpointOptions endpoint,
        ModbusDataArea area,
        int address)
        => (BaseAddress(endpoint, area) + address).ToString(CultureInfo.InvariantCulture);

    private static int BaseAddress(ModbusEndpointOptions endpoint, ModbusDataArea area)
        => area == ModbusDataArea.Coil
            ? endpoint.CoilStartAddress
            : endpoint.HoldingRegisterStartAddress;

    private static ModbusDataArea? ResolveArea(
        int physical,
        ModbusEndpointOptions endpoint,
        ModbusDataArea currentArea)
    {
        var isCoil = Contains(endpoint.CoilStartAddress, endpoint.CoilCount, physical);
        var isRegister = Contains(endpoint.HoldingRegisterStartAddress, endpoint.RegisterCount, physical);

        return (isCoil, isRegister) switch
        {
            (false, false) => null,
            (true, false) => ModbusDataArea.Coil,
            (false, true) => ModbusDataArea.HoldingRegister,
            (true, true) => currentArea,
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

    private void NormalizeAlarmBitIndex() => SetAlarmBitIndex(AlarmUsesRegisterBit ? _alarmBitIndex ?? 0 : null);
    private void NormalizeAcknowledgementBitIndex() => SetAcknowledgementBitIndex(AcknowledgementUsesRegisterBit ? _acknowledgementBitIndex ?? 0 : null);

    private void SetAlarmBitIndex(int? value)
    {
        if (_alarmBitIndex == value)
        {
            return;
        }

        this.RaiseAndSetIfChanged(ref _alarmBitIndex, value, nameof(AlarmBitIndex));
        RaiseAlarmAddressState();
    }

    private void SetAcknowledgementBitIndex(int? value)
    {
        if (_acknowledgementBitIndex == value)
        {
            return;
        }

        this.RaiseAndSetIfChanged(ref _acknowledgementBitIndex, value, nameof(AcknowledgementBitIndex));
        RaiseAcknowledgementAddressState();
    }

    private void SetPhysicalAddressError(string? message)
    {
        if (_physicalAddressError == message)
        {
            return;
        }

        this.RaiseAndSetIfChanged(ref _physicalAddressError, message, nameof(PhysicalAddressError));
    }

    private void ClearPhysicalAddressError() => SetPhysicalAddressError(null);

    private void RaiseAlarmAddressState()
    {
        this.RaisePropertyChanged(nameof(CanEditAlarmBitIndex));
        RaiseAlarmPhysicalAddressText();
    }

    private void RaiseAcknowledgementAddressState()
    {
        this.RaisePropertyChanged(nameof(CanEditAcknowledgementBitIndex));
        RaiseAcknowledgementPhysicalAddressText();
    }

    private void RaiseAlarmPhysicalAddressText()
    {
        this.RaisePropertyChanged(nameof(AlarmClientPhysicalAddressText));
        this.RaisePropertyChanged(nameof(AlarmServerPhysicalAddressText));
    }

    private void RaiseAcknowledgementPhysicalAddressText()
    {
        this.RaisePropertyChanged(nameof(AcknowledgementClientPhysicalAddressText));
        this.RaisePropertyChanged(nameof(AcknowledgementServerPhysicalAddressText));
    }
}
