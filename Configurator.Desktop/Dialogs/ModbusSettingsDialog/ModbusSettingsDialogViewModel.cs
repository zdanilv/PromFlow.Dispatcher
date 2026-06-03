using Configurator.Application.Services;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace Configurator.Desktop.Dialogs.ModbusSettingsDialog;

/// <summary>
/// Модель представления диалога настроек Modbus: редактирует endpoints клиента/сервера и карту данных.
/// </summary>
public sealed class ModbusSettingsDialogViewModel : ReactiveObject
{
    private const int MinPollIntervalMs = 100;
    private const int MaxPollIntervalMs = 60000;
    private const int MinWriteConfirmationTimeoutMs = 100;
    private const int MaxWriteConfirmationTimeoutMs = 60000;
    private const int MaxCoils = 2000;
    private const int MaxRegisters = 123;

    private readonly IAppConfigService _appConfigService;
    private readonly IModbusDataMapValidator _dataMapValidator;
    private readonly Subject<ModbusOptions?> _result = new();
    private bool _autostartOnWorkspaceOpen;
    private ModbusRunMode _startupMode;
    private int _writeConfirmationTimeoutMs;
    private bool _clientEnabled;
    private string _clientHost = "127.0.0.1";
    private int _clientPort;
    private int _clientUnitId;
    private int _clientPollIntervalMs;
    private bool _clientCoilsEnabled;
    private bool _clientHoldingRegistersEnabled;
    private int _clientCoilStartAddress;
    private int _clientHoldingRegisterStartAddress;
    private int _clientCoilCount;
    private int _clientRegisterCount;
    private bool _serverEnabled;
    private string _serverBindAddress = "127.0.0.1";
    private int _serverPort;
    private int _serverUnitId;
    private int _serverPollIntervalMs;
    private bool _serverCoilsEnabled;
    private bool _serverHoldingRegistersEnabled;
    private int _serverCoilStartAddress;
    private int _serverHoldingRegisterStartAddress;
    private int _serverCoilCount;
    private int _serverRegisterCount;
    private string _errorText = string.Empty;

    /// <summary>
    /// Создает диалог для указанной секции и заполняет поля из текущих настроек.
    /// </summary>
    public ModbusSettingsDialogViewModel(
        string title,
        string sectionName,
        ModbusOptions options,
        IAppConfigService appConfigService,
        IModbusDataMapValidator dataMapValidator)
    {
        Title = title;
        SectionName = sectionName;
        _appConfigService = appConfigService;
        _dataMapValidator = dataMapValidator;

        ApplyOptions(options.Clone());

        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        CloseCommand = ReactiveCommand.Create(() => _result.OnNext(null));
        AddDataPointCommand = ReactiveCommand.Create(AddDataPoint);
    }

    public string Title { get; }

    public string SectionName { get; }

    public IReadOnlyList<ModbusRunMode> StartupModes { get; } = Enum.GetValues<ModbusRunMode>();

    public ObservableCollection<ModbusDataPointEditorRow> DataPoints { get; } = new();

    public ReactiveCommand<Unit, Unit> SaveCommand { get; }

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public ReactiveCommand<Unit, Unit> AddDataPointCommand { get; }

    public IObservable<ModbusOptions?> Result => _result;

    public bool AutostartOnWorkspaceOpen
    {
        get => _autostartOnWorkspaceOpen;
        set => this.RaiseAndSetIfChanged(ref _autostartOnWorkspaceOpen, value);
    }

    public ModbusRunMode StartupMode
    {
        get => _startupMode;
        set => this.RaiseAndSetIfChanged(ref _startupMode, value);
    }

    public int WriteConfirmationTimeoutMs
    {
        get => _writeConfirmationTimeoutMs;
        set => this.RaiseAndSetIfChanged(ref _writeConfirmationTimeoutMs, value);
    }

    public bool ClientEnabled
    {
        get => _clientEnabled;
        set => this.RaiseAndSetIfChanged(ref _clientEnabled, value);
    }

    public string ClientHost
    {
        get => _clientHost;
        set => this.RaiseAndSetIfChanged(ref _clientHost, value);
    }

    public int ClientPort
    {
        get => _clientPort;
        set => this.RaiseAndSetIfChanged(ref _clientPort, value);
    }

    public int ClientUnitId
    {
        get => _clientUnitId;
        set => this.RaiseAndSetIfChanged(ref _clientUnitId, value);
    }

    public int ClientPollIntervalMs
    {
        get => _clientPollIntervalMs;
        set => this.RaiseAndSetIfChanged(ref _clientPollIntervalMs, value);
    }

    public bool ClientCoilsEnabled
    {
        get => _clientCoilsEnabled;
        set => this.RaiseAndSetIfChanged(ref _clientCoilsEnabled, value);
    }

    public bool ClientHoldingRegistersEnabled
    {
        get => _clientHoldingRegistersEnabled;
        set => this.RaiseAndSetIfChanged(ref _clientHoldingRegistersEnabled, value);
    }

    public int ClientCoilStartAddress
    {
        get => _clientCoilStartAddress;
        set => this.RaiseAndSetIfChanged(ref _clientCoilStartAddress, value);
    }

    public int ClientHoldingRegisterStartAddress
    {
        get => _clientHoldingRegisterStartAddress;
        set => this.RaiseAndSetIfChanged(ref _clientHoldingRegisterStartAddress, value);
    }

    public int ClientCoilCount
    {
        get => _clientCoilCount;
        set => this.RaiseAndSetIfChanged(ref _clientCoilCount, value);
    }

    public int ClientRegisterCount
    {
        get => _clientRegisterCount;
        set => this.RaiseAndSetIfChanged(ref _clientRegisterCount, value);
    }

    public bool ServerEnabled
    {
        get => _serverEnabled;
        set => this.RaiseAndSetIfChanged(ref _serverEnabled, value);
    }

    public string ServerBindAddress
    {
        get => _serverBindAddress;
        set => this.RaiseAndSetIfChanged(ref _serverBindAddress, value);
    }

    public int ServerPort
    {
        get => _serverPort;
        set => this.RaiseAndSetIfChanged(ref _serverPort, value);
    }

    public int ServerUnitId
    {
        get => _serverUnitId;
        set => this.RaiseAndSetIfChanged(ref _serverUnitId, value);
    }

    public int ServerPollIntervalMs
    {
        get => _serverPollIntervalMs;
        set => this.RaiseAndSetIfChanged(ref _serverPollIntervalMs, value);
    }

    public bool ServerCoilsEnabled
    {
        get => _serverCoilsEnabled;
        set => this.RaiseAndSetIfChanged(ref _serverCoilsEnabled, value);
    }

    public bool ServerHoldingRegistersEnabled
    {
        get => _serverHoldingRegistersEnabled;
        set => this.RaiseAndSetIfChanged(ref _serverHoldingRegistersEnabled, value);
    }

    public int ServerCoilStartAddress
    {
        get => _serverCoilStartAddress;
        set => this.RaiseAndSetIfChanged(ref _serverCoilStartAddress, value);
    }

    public int ServerHoldingRegisterStartAddress
    {
        get => _serverHoldingRegisterStartAddress;
        set => this.RaiseAndSetIfChanged(ref _serverHoldingRegisterStartAddress, value);
    }

    public int ServerCoilCount
    {
        get => _serverCoilCount;
        set => this.RaiseAndSetIfChanged(ref _serverCoilCount, value);
    }

    public int ServerRegisterCount
    {
        get => _serverRegisterCount;
        set => this.RaiseAndSetIfChanged(ref _serverRegisterCount, value);
    }

    public string ErrorText
    {
        get => _errorText;
        private set => this.RaiseAndSetIfChanged(ref _errorText, value);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        var options = BuildOptions();
        var validationError = Validate(options);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            ErrorText = validationError;
            return;
        }

        try
        {
            await _appConfigService.SaveSectionAsync(SectionName, options, ct);
            ErrorText = string.Empty;
            _result.OnNext(options.Clone());
        }
        catch (Exception ex)
        {
            ErrorText = $"Не удалось сохранить настройки: {ex.Message}";
        }
    }

    private void ApplyOptions(ModbusOptions options)
    {
        AutostartOnWorkspaceOpen = options.AutostartOnWorkspaceOpen;
        StartupMode = options.StartupMode;
        WriteConfirmationTimeoutMs = options.WriteConfirmationTimeoutMs;

        ClientEnabled = options.Client.Enabled;
        ClientHost = options.Client.Host;
        ClientPort = options.Client.Port;
        ClientUnitId = options.Client.UnitId;
        ClientPollIntervalMs = options.Client.PollIntervalMs;
        ClientCoilsEnabled = options.Client.CoilsEnabled;
        ClientHoldingRegistersEnabled = options.Client.HoldingRegistersEnabled;
        ClientCoilStartAddress = options.Client.CoilStartAddress;
        ClientHoldingRegisterStartAddress = options.Client.HoldingRegisterStartAddress;
        ClientCoilCount = options.Client.CoilCount;
        ClientRegisterCount = options.Client.RegisterCount;

        ServerEnabled = options.Server.Enabled;
        ServerBindAddress = options.Server.BindAddress;
        ServerPort = options.Server.Port;
        ServerUnitId = options.Server.UnitId;
        ServerPollIntervalMs = options.Server.PollIntervalMs;
        ServerCoilsEnabled = options.Server.CoilsEnabled;
        ServerHoldingRegistersEnabled = options.Server.HoldingRegistersEnabled;
        ServerCoilStartAddress = options.Server.CoilStartAddress;
        ServerHoldingRegisterStartAddress = options.Server.HoldingRegisterStartAddress;
        ServerCoilCount = options.Server.CoilCount;
        ServerRegisterCount = options.Server.RegisterCount;

        DataPoints.Clear();
        foreach (var point in options.DataMap)
        {
            DataPoints.Add(new ModbusDataPointEditorRow(point.Clone(), RemoveDataPoint));
        }
    }

    /// <summary>
    /// Собирает полную копию ModbusOptions из полей диалога.
    /// </summary>
    private ModbusOptions BuildOptions()
        => new()
        {
            AutostartOnWorkspaceOpen = AutostartOnWorkspaceOpen,
            StartupMode = StartupMode,
            WriteConfirmationTimeoutMs = WriteConfirmationTimeoutMs,
            Client = new ModbusEndpointOptions
            {
                Enabled = ClientEnabled,
                Host = NormalizeAddress(ClientHost),
                Port = ClientPort,
                UnitId = ClientUnitId,
                PollIntervalMs = ClientPollIntervalMs,
                CoilsEnabled = ClientCoilsEnabled,
                HoldingRegistersEnabled = ClientHoldingRegistersEnabled,
                CoilStartAddress = ClientCoilStartAddress,
                HoldingRegisterStartAddress = ClientHoldingRegisterStartAddress,
                CoilCount = ClientCoilCount,
                RegisterCount = ClientRegisterCount
            },
            Server = new ModbusEndpointOptions
            {
                Enabled = ServerEnabled,
                BindAddress = NormalizeAddress(ServerBindAddress),
                Port = ServerPort,
                UnitId = ServerUnitId,
                PollIntervalMs = ServerPollIntervalMs,
                CoilsEnabled = ServerCoilsEnabled,
                HoldingRegistersEnabled = ServerHoldingRegistersEnabled,
                CoilStartAddress = ServerCoilStartAddress,
                HoldingRegisterStartAddress = ServerHoldingRegisterStartAddress,
                CoilCount = ServerCoilCount,
                RegisterCount = ServerRegisterCount
            },
            DataMap = DataPoints.Select(point => point.ToOptions()).ToList()
        };

    private string Validate(ModbusOptions options)
    {
        // Сначала проверяются endpoint-поля UI, затем доменная карта данных через общий validator.
        if (!IsInRange(options.WriteConfirmationTimeoutMs, MinWriteConfirmationTimeoutMs, MaxWriteConfirmationTimeoutMs))
        {
            return $"Таймаут подтверждения записи должен быть от {MinWriteConfirmationTimeoutMs} до {MaxWriteConfirmationTimeoutMs} мс.";
        }

        var clientError = ValidateEndpoint("Клиент", options.Client, validateHost: true);
        if (!string.IsNullOrWhiteSpace(clientError))
        {
            return clientError;
        }

        var serverError = ValidateEndpoint("Сервер", options.Server, validateHost: false);
        if (!string.IsNullOrWhiteSpace(serverError))
        {
            return serverError;
        }

        if (options.Client.Enabled)
        {
            var result = _dataMapValidator.Validate(options, ModbusRunMode.Client);
            if (!result.Succeeded)
            {
                return OperationMessage(result);
            }
        }

        if (options.Server.Enabled)
        {
            var result = _dataMapValidator.Validate(options, ModbusRunMode.Server);
            if (!result.Succeeded)
            {
                return OperationMessage(result);
            }
        }

        return string.Empty;
    }

    private static string ValidateEndpoint(string title, ModbusEndpointOptions endpoint, bool validateHost)
    {
        var address = validateHost ? endpoint.Host : endpoint.BindAddress;
        if (string.IsNullOrWhiteSpace(address))
        {
            return $"{title}: адрес не должен быть пустым.";
        }

        if (!IsInRange(endpoint.Port, 1, 65535))
        {
            return $"{title}: порт должен быть от 1 до 65535.";
        }

        if (!IsInRange(endpoint.UnitId, 1, 247))
        {
            return $"{title}: UnitId должен быть от 1 до 247.";
        }

        if (!IsInRange(endpoint.PollIntervalMs, MinPollIntervalMs, MaxPollIntervalMs))
        {
            return $"{title}: интервал опроса должен быть от {MinPollIntervalMs} до {MaxPollIntervalMs} мс.";
        }

        if (!IsInRange(endpoint.CoilStartAddress, 0, ushort.MaxValue)
            || !IsInRange(endpoint.HoldingRegisterStartAddress, 0, ushort.MaxValue))
        {
            return $"{title}: начальные адреса должны быть неотрицательными.";
        }

        if (!IsInRange(endpoint.CoilCount, 0, MaxCoils))
        {
            return $"{title}: количество Coils должно быть от 0 до {MaxCoils}.";
        }

        if (!IsInRange(endpoint.RegisterCount, 0, MaxRegisters))
        {
            return $"{title}: количество Holding Registers должно быть от 0 до {MaxRegisters}.";
        }

        return string.Empty;
    }

    private void AddDataPoint()
    {
        DataPoints.Add(new ModbusDataPointEditorRow(
            new ModbusDataPointOptions
            {
                Name = $"Point{DataPoints.Count + 1}",
                Area = ModbusDataArea.HoldingRegister,
                Address = 0,
                Length = 1,
                Access = ModbusDataAccess.ReadWrite,
                Type = ModbusValueType.UInt16
            },
            RemoveDataPoint));
    }

    private void RemoveDataPoint(ModbusDataPointEditorRow row)
    {
        DataPoints.Remove(row);
    }

    private static string NormalizeAddress(string value)
        => string.IsNullOrWhiteSpace(value) ? "127.0.0.1" : value.Trim();

    private static bool IsInRange(int value, int min, int max)
        => value >= min && value <= max;

    private static string OperationMessage(ModbusOperationResult result)
        => result.ErrorDetails is { Length: > 0 } details
            ? $"{result.ErrorMessage ?? "Ошибка проверки карты Modbus."} {details}"
            : result.ErrorMessage ?? "Ошибка проверки карты Modbus.";
}

/// <summary>
/// Редактируемая строка карты данных Modbus в диалоге настроек.
/// </summary>
public sealed class ModbusDataPointEditorRow : ReactiveObject
{
    private string _name;
    private ModbusDataArea _area;
    private int _address;
    private int _length;
    private ModbusDataAccess _access;
    private ModbusValueType _type;

    /// <summary>
    /// Создает строку редактора из настроенной точки данных.
    /// </summary>
    public ModbusDataPointEditorRow(
        ModbusDataPointOptions options,
        Action<ModbusDataPointEditorRow> remove)
    {
        _name = options.Name;
        _area = options.Area;
        _address = options.Address;
        _length = options.Length;
        _access = options.Access;
        _type = options.Type;
        RemoveCommand = ReactiveCommand.Create(() => remove(this));
    }

    public IReadOnlyList<ModbusDataArea> DataAreas { get; } = Enum.GetValues<ModbusDataArea>();

    public IReadOnlyList<ModbusDataAccess> DataAccessModes { get; } = Enum.GetValues<ModbusDataAccess>();

    public IReadOnlyList<ModbusValueType> ValueTypes { get; } = Enum.GetValues<ModbusValueType>();

    public ReactiveCommand<Unit, Unit> RemoveCommand { get; }

    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    public ModbusDataArea Area
    {
        get => _area;
        set => this.RaiseAndSetIfChanged(ref _area, value);
    }

    public int Address
    {
        get => _address;
        set => this.RaiseAndSetIfChanged(ref _address, value);
    }

    public int Length
    {
        get => _length;
        set => this.RaiseAndSetIfChanged(ref _length, value);
    }

    public ModbusDataAccess Access
    {
        get => _access;
        set => this.RaiseAndSetIfChanged(ref _access, value);
    }

    public ModbusValueType Type
    {
        get => _type;
        set => this.RaiseAndSetIfChanged(ref _type, value);
    }

    /// <summary>
    /// Преобразует значения строки обратно в доменные настройки точки данных.
    /// </summary>
    public ModbusDataPointOptions ToOptions()
        => new()
        {
            Name = Name.Trim(),
            Area = Area,
            Address = Address,
            Length = Length,
            Access = Access,
            Type = Type
        };
}
