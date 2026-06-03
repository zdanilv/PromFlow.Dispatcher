using Avalonia.Threading;
using Configurator.Application.Services;
using Configurator.Application.Services.Dialogs;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Microsoft.Extensions.Options;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace Configurator.Desktop.Workspace.Modbus;

/// <summary>
/// Модель представления экрана Modbus: управляет настройками, командами клиента/сервера и таблицами значений.
/// </summary>
public sealed class ModbusViewModel : ViewModelBase, IDisposable
{
    private readonly IModbusRuntimeService _runtimeService;
    private readonly IModbusClientService _clientService;
    private readonly IModbusServerService _serverService;
    private readonly IAppConfigService _appConfigService;
    private readonly IDialogService _dialogService;
    private ModbusOptions _currentOptions = new();

    private string _clientHost = "127.0.0.1";
    private int _clientPort = 502;
    private int _clientUnitId = 1;
    private int _clientPollIntervalMs = 500;
    private int _clientCoilCount = 10;
    private int _clientRegisterCount = 20;
    private bool _clientCoilsEnabled = true;
    private bool _clientHoldingRegistersEnabled = true;
    private int _clientCoilStartAddress;
    private int _clientHoldingRegisterStartAddress;
    private bool _clientEnabled = true;
    private bool _serverEnabled = true;
    private bool _serverCoilsEnabled = true;
    private bool _serverHoldingRegistersEnabled = true;
    private string _serverBindAddress = "127.0.0.1";
    private int _serverPort = 502;
    private int _serverUnitId = 1;
    private int _serverPollIntervalMs = 500;
    private int _serverCoilCount = 10;
    private int _serverRegisterCount = 20;
    private int _serverCoilStartAddress;
    private int _serverHoldingRegisterStartAddress;
    private string _clientState = ModbusConnectionState.Stopped.ToString();
    private string _serverState = ModbusConnectionState.Stopped.ToString();
    private string _clientMessage = "Клиент остановлен";
    private string _serverMessage = "Сервер остановлен";
    private string _lastError = string.Empty;
    private string _lastUpdated = "-";
    private string _clientLastUpdated = "-";
    private string _serverLastUpdated = "-";
    private bool _isClientCommandRunning;
    private bool _isServerCommandRunning;
    private bool _isBothCommandRunning;
    private bool _isSettingsCommandRunning;
    private readonly Dictionary<string, DateTimeOffset> _eventTimes = new();

    /// <summary>
    /// Создает экран Modbus и подписывается на события runtime.
    /// </summary>
    public ModbusViewModel(
        IModbusRuntimeService runtimeService,
        IModbusClientService clientService,
        IModbusServerService serverService,
        IOptionsMonitor<ModbusOptions> optionsMonitor,
        IAppConfigService appConfigService,
        IDialogService dialogService)
    {
        _runtimeService = runtimeService;
        _clientService = clientService;
        _serverService = serverService;
        _appConfigService = appConfigService;
        _dialogService = dialogService;

        ApplyOptions(optionsMonitor.CurrentValue);
        ApplyStatus(_runtimeService.Status);
        ApplySnapshot(_runtimeService.ClientSnapshot);
        ApplySnapshot(_runtimeService.ServerSnapshot);

        _runtimeService.StatusChanged += OnRuntimeStatusChanged;
        _runtimeService.SnapshotChanged += OnRuntimeSnapshotChanged;

        var canStartBoth = this.WhenAnyValue(x => x.CanStartBoth);
        var canStopBoth = this.WhenAnyValue(x => x.CanStopBoth);
        var canRestartBoth = this.WhenAnyValue(x => x.CanRestartBoth);
        var canStartClient = this.WhenAnyValue(x => x.CanStartClient);
        var canStopClient = this.WhenAnyValue(x => x.CanStopClient);
        var canRestartClient = this.WhenAnyValue(x => x.CanRestartClient);
        var canStartServer = this.WhenAnyValue(x => x.CanStartServer);
        var canStopServer = this.WhenAnyValue(x => x.CanStopServer);
        var canRestartServer = this.WhenAnyValue(x => x.CanRestartServer);
        var canOpenSettings = this.WhenAnyValue(x => x.CanOpenSettings);

        StartBothCommand = ReactiveCommand.CreateFromTask(() => RunBothCommandAsync("Запуск клиента и сервера", StartBothAsync), canStartBoth);
        StopBothCommand = ReactiveCommand.CreateFromTask(() => RunBothCommandAsync("Остановка клиента и сервера", () => _runtimeService.StopAsync()), canStopBoth);
        RestartBothCommand = ReactiveCommand.CreateFromTask(() => RunBothCommandAsync("Перезапуск клиента и сервера", RestartBothAsync), canRestartBoth);
        StartClientCommand = ReactiveCommand.CreateFromTask(() => RunClientCommandAsync("Запуск клиента", StartClientAsync), canStartClient);
        StopClientCommand = ReactiveCommand.CreateFromTask(() => RunClientCommandAsync("Остановка клиента", () => _runtimeService.StopClientAsync()), canStopClient);
        RestartClientCommand = ReactiveCommand.CreateFromTask(() => RunClientCommandAsync("Перезапуск клиента", RestartClientAsync), canRestartClient);
        StartServerCommand = ReactiveCommand.CreateFromTask(() => RunServerCommandAsync("Запуск сервера", StartServerAsync), canStartServer);
        StopServerCommand = ReactiveCommand.CreateFromTask(() => RunServerCommandAsync("Остановка сервера", () => _runtimeService.StopServerAsync()), canStopServer);
        RestartServerCommand = ReactiveCommand.CreateFromTask(() => RunServerCommandAsync("Перезапуск сервера", RestartServerAsync), canRestartServer);
        OpenSettingsCommand = ReactiveCommand.CreateFromTask(OpenSettingsAsync, canOpenSettings);

        RaiseCommandStateChanged();
    }

    /// <summary>
    /// Coils, прочитанные клиентом с внешнего сервера.
    /// </summary>
    public ObservableCollection<ModbusCoilRow> ClientCoils { get; } = new();

    /// <summary>
    /// Raw Holding Registers, прочитанные клиентом.
    /// </summary>
    public ObservableCollection<ModbusRegisterRow> ClientRegisters { get; } = new();

    /// <summary>
    /// Типизированные записи клиента.
    /// </summary>
    public ObservableCollection<ModbusTypedRegisterRow> ClientTypedRegisters { get; } = new();

    /// <summary>
    /// Coils локальной карты сервера.
    /// </summary>
    public ObservableCollection<ModbusCoilRow> ServerCoils { get; } = new();

    /// <summary>
    /// Raw Holding Registers локальной карты сервера.
    /// </summary>
    public ObservableCollection<ModbusRegisterRow> ServerRegisters { get; } = new();

    /// <summary>
    /// Типизированные записи сервера.
    /// </summary>
    public ObservableCollection<ModbusTypedRegisterRow> ServerTypedRegisters { get; } = new();

    /// <summary>
    /// Общий журнал событий клиента и сервера.
    /// </summary>
    public ObservableCollection<ModbusEventRow> Events { get; } = new();

    public ReactiveCommand<Unit, Unit> StartBothCommand { get; }
    public ReactiveCommand<Unit, Unit> StopBothCommand { get; }
    public ReactiveCommand<Unit, Unit> RestartBothCommand { get; }
    public ReactiveCommand<Unit, Unit> StartClientCommand { get; }
    public ReactiveCommand<Unit, Unit> StopClientCommand { get; }
    public ReactiveCommand<Unit, Unit> RestartClientCommand { get; }
    public ReactiveCommand<Unit, Unit> StartServerCommand { get; }
    public ReactiveCommand<Unit, Unit> StopServerCommand { get; }
    public ReactiveCommand<Unit, Unit> RestartServerCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenSettingsCommand { get; }

    public bool CanStartClient => !IsClientCommandRunning && !IsBothCommandRunning && IsStartable(ParseState(ClientState));
    public bool CanStopClient => !IsClientCommandRunning && !IsBothCommandRunning && IsStoppable(ParseState(ClientState));
    public bool CanRestartClient => !IsClientCommandRunning && !IsBothCommandRunning && ParseState(ClientState) != ModbusConnectionState.Stopped;
    public bool CanStartServer => !IsServerCommandRunning && !IsBothCommandRunning && IsStartable(ParseState(ServerState));
    public bool CanStopServer => !IsServerCommandRunning && !IsBothCommandRunning && IsStoppable(ParseState(ServerState));
    public bool CanRestartServer => !IsServerCommandRunning && !IsBothCommandRunning && ParseState(ServerState) != ModbusConnectionState.Stopped;
    public bool CanStartBoth => !IsBothCommandRunning && !IsClientCommandRunning && !IsServerCommandRunning && (IsStartable(ParseState(ClientState)) || IsStartable(ParseState(ServerState)));
    public bool CanStopBoth => !IsBothCommandRunning && !IsClientCommandRunning && !IsServerCommandRunning && (IsStoppable(ParseState(ClientState)) || IsStoppable(ParseState(ServerState)));
    public bool CanRestartBoth => !IsBothCommandRunning && !IsClientCommandRunning && !IsServerCommandRunning && (ParseState(ClientState) != ModbusConnectionState.Stopped || ParseState(ServerState) != ModbusConnectionState.Stopped);
    public bool CanOpenSettings => !IsSettingsCommandRunning && !IsBothCommandRunning && !IsClientCommandRunning && !IsServerCommandRunning && IsConfigurable(ParseState(ClientState)) && IsConfigurable(ParseState(ServerState));
    public bool CanEditClientValues => ParseState(ClientState) == ModbusConnectionState.Running && !IsClientCommandRunning && !IsBothCommandRunning;
    public bool CanEditClientCoils => CanEditClientValues && ClientCoilsEnabled;
    public bool CanEditClientRegisters => CanEditClientValues && ClientHoldingRegistersEnabled;
    public bool CanEditServerValues => ParseState(ServerState) == ModbusConnectionState.Running && !IsServerCommandRunning && !IsBothCommandRunning;

    public bool IsClientCommandRunning
    {
        get => _isClientCommandRunning;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isClientCommandRunning, value);
            RaiseCommandStateChanged();
        }
    }

    public bool IsServerCommandRunning
    {
        get => _isServerCommandRunning;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isServerCommandRunning, value);
            RaiseCommandStateChanged();
        }
    }

    public bool IsBothCommandRunning
    {
        get => _isBothCommandRunning;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isBothCommandRunning, value);
            RaiseCommandStateChanged();
        }
    }

    public bool IsSettingsCommandRunning
    {
        get => _isSettingsCommandRunning;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isSettingsCommandRunning, value);
            RaiseCommandStateChanged();
        }
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

    public bool ClientCoilsEnabled
    {
        get => _clientCoilsEnabled;
        set
        {
            this.RaiseAndSetIfChanged(ref _clientCoilsEnabled, value);
            RaiseCommandStateChanged();
        }
    }

    public bool ClientHoldingRegistersEnabled
    {
        get => _clientHoldingRegistersEnabled;
        set
        {
            this.RaiseAndSetIfChanged(ref _clientHoldingRegistersEnabled, value);
            RaiseCommandStateChanged();
        }
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

    public bool ClientEnabled
    {
        get => _clientEnabled;
        set => this.RaiseAndSetIfChanged(ref _clientEnabled, value);
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

    public string ClientState
    {
        get => _clientState;
        private set
        {
            this.RaiseAndSetIfChanged(ref _clientState, value);
            RaiseCommandStateChanged();
        }
    }

    public string ServerState
    {
        get => _serverState;
        private set
        {
            this.RaiseAndSetIfChanged(ref _serverState, value);
            RaiseCommandStateChanged();
        }
    }

    public string ClientMessage
    {
        get => _clientMessage;
        private set => this.RaiseAndSetIfChanged(ref _clientMessage, value);
    }

    public string ServerMessage
    {
        get => _serverMessage;
        private set => this.RaiseAndSetIfChanged(ref _serverMessage, value);
    }

    public string LastError
    {
        get => _lastError;
        private set => this.RaiseAndSetIfChanged(ref _lastError, value);
    }

    public string LastUpdated
    {
        get => _lastUpdated;
        private set => this.RaiseAndSetIfChanged(ref _lastUpdated, value);
    }

    public string ClientLastUpdated
    {
        get => _clientLastUpdated;
        private set => this.RaiseAndSetIfChanged(ref _clientLastUpdated, value);
    }

    public string ServerLastUpdated
    {
        get => _serverLastUpdated;
        private set => this.RaiseAndSetIfChanged(ref _serverLastUpdated, value);
    }

    /// <summary>
    /// Отписывается от событий runtime.
    /// </summary>
    public void Dispose()
    {
        _runtimeService.StatusChanged -= OnRuntimeStatusChanged;
        _runtimeService.SnapshotChanged -= OnRuntimeSnapshotChanged;
    }

    private async Task StartBothAsync()
    {
        RebuildAllRowsFromSettings();
        await _runtimeService.StartAsync(ModbusRunMode.Both, BuildOptions());
    }

    private async Task RestartBothAsync()
    {
        RebuildAllRowsFromSettings();
        await _runtimeService.RestartAsync(ModbusRunMode.Both, BuildOptions());
    }

    private async Task StartClientAsync()
    {
        RebuildClientRowsFromSettings();
        await _runtimeService.StartClientAsync(BuildOptions());
    }

    private async Task RestartClientAsync()
    {
        RebuildClientRowsFromSettings();
        await _runtimeService.RestartClientAsync(BuildOptions());
    }

    private async Task StartServerAsync()
    {
        RebuildServerRowsFromSettings();
        await _runtimeService.StartServerAsync(BuildOptions());
    }

    private async Task RestartServerAsync()
    {
        RebuildServerRowsFromSettings();
        await _runtimeService.RestartServerAsync(BuildOptions());
    }

    private async Task OpenSettingsAsync()
    {
        IsSettingsCommandRunning = true;

        try
        {
            var options = _appConfigService.GetSection<ModbusOptions>(ModbusOptions.SectionName);
            var savedOptions = await _dialogService.EditModbusSettingsAsync(
                "Настройки Modbus TCP",
                ModbusOptions.SectionName,
                options);

            if (savedOptions is null)
            {
                return;
            }

            ApplyOptions(savedOptions);
            AddEvent("UI", "Настройки Modbus TCP сохранены");
        }
        catch (Exception ex)
        {
            SetLastError(ex.Message);
            AddEvent("Ошибка настроек", LastError, "Error", ex.ToString());
            await _dialogService.ShowErrorAsync(
                "Modbus TCP",
                "Не удалось открыть или сохранить настройки.",
                ex.Message);
        }
        finally
        {
            IsSettingsCommandRunning = false;
        }
    }

    private async Task RunAsync(string eventText, Func<Task> action)
    {
        AddEvent("UI", eventText);

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            SetLastError(ex.Message);
            AddEvent("Ошибка", LastError, "Error", ex.ToString());
        }
    }

    private async Task RunClientCommandAsync(string eventText, Func<Task> action)
    {
        IsClientCommandRunning = true;

        try
        {
            await RunAsync(eventText, action);
        }
        finally
        {
            IsClientCommandRunning = false;
        }
    }

    private async Task RunServerCommandAsync(string eventText, Func<Task> action)
    {
        IsServerCommandRunning = true;

        try
        {
            await RunAsync(eventText, action);
        }
        finally
        {
            IsServerCommandRunning = false;
        }
    }

    private async Task RunBothCommandAsync(string eventText, Func<Task> action)
    {
        IsBothCommandRunning = true;

        try
        {
            await RunAsync(eventText, action);
        }
        finally
        {
            IsBothCommandRunning = false;
        }
    }

    private ModbusOptions BuildOptions()
    {
        var options = _currentOptions.Clone();
        options.Client = new ModbusEndpointOptions
        {
            Enabled = ClientEnabled,
            Host = NormalizeAddress(ClientHost),
            Port = Clamp(ClientPort, 1, 65535),
            UnitId = Clamp(ClientUnitId, 1, 247),
            PollIntervalMs = Clamp(ClientPollIntervalMs, 100, 60000),
            CoilsEnabled = ClientCoilsEnabled,
            HoldingRegistersEnabled = ClientHoldingRegistersEnabled,
            CoilStartAddress = Clamp(ClientCoilStartAddress, 0, ushort.MaxValue),
            HoldingRegisterStartAddress = Clamp(ClientHoldingRegisterStartAddress, 0, ushort.MaxValue),
            CoilCount = Clamp(ClientCoilCount, 0, 2000),
            RegisterCount = Clamp(ClientRegisterCount, 0, 123)
        };
        options.Server = new ModbusEndpointOptions
        {
            Enabled = ServerEnabled,
            BindAddress = NormalizeAddress(ServerBindAddress),
            Port = Clamp(ServerPort, 1, 65535),
            UnitId = Clamp(ServerUnitId, 1, 247),
            PollIntervalMs = Clamp(ServerPollIntervalMs, 100, 60000),
            CoilsEnabled = _serverCoilsEnabled,
            HoldingRegistersEnabled = _serverHoldingRegistersEnabled,
            CoilStartAddress = Clamp(ServerCoilStartAddress, 0, ushort.MaxValue),
            HoldingRegisterStartAddress = Clamp(ServerHoldingRegisterStartAddress, 0, ushort.MaxValue),
            CoilCount = Clamp(ServerCoilCount, 0, 2000),
            RegisterCount = Clamp(ServerRegisterCount, 0, 123)
        };
        return options;
    }

    private void ApplyOptions(ModbusOptions options)
    {
        _currentOptions = options.Clone();
        ClientEnabled = options.Client.Enabled;
        ClientHost = options.Client.Host;
        ClientPort = options.Client.Port;
        ClientUnitId = options.Client.UnitId;
        ClientPollIntervalMs = options.Client.PollIntervalMs;
        ClientCoilCount = options.Client.CoilCount;
        ClientRegisterCount = options.Client.RegisterCount;
        ClientCoilsEnabled = options.Client.CoilsEnabled;
        ClientHoldingRegistersEnabled = options.Client.HoldingRegistersEnabled;
        ClientCoilStartAddress = options.Client.CoilStartAddress;
        ClientHoldingRegisterStartAddress = options.Client.HoldingRegisterStartAddress;
        ServerEnabled = options.Server.Enabled;
        _serverCoilsEnabled = options.Server.CoilsEnabled;
        _serverHoldingRegistersEnabled = options.Server.HoldingRegistersEnabled;
        ServerBindAddress = options.Server.BindAddress;
        ServerPort = options.Server.Port;
        ServerUnitId = options.Server.UnitId;
        ServerPollIntervalMs = options.Server.PollIntervalMs;
        ServerCoilCount = options.Server.CoilCount;
        ServerRegisterCount = options.Server.RegisterCount;
        ServerCoilStartAddress = options.Server.CoilStartAddress;
        ServerHoldingRegisterStartAddress = options.Server.HoldingRegisterStartAddress;
        RebuildAllRowsFromSettings();
    }

    private void OnRuntimeStatusChanged(object? sender, ModbusStatus status)
    {
        Dispatcher.UIThread.Post(() => ApplyStatus(status));
    }

    private void OnRuntimeSnapshotChanged(object? sender, ModbusSnapshot snapshot)
    {
        Dispatcher.UIThread.Post(() => ApplySnapshot(snapshot));
    }

    private void ApplyStatus(ModbusStatus status)
    {
        ClientState = status.ClientState.ToString();
        ServerState = status.ServerState.ToString();
        ClientMessage = status.ClientMessage;
        ServerMessage = status.ServerMessage;
        SetLastError(status.LastError);
        LastUpdated = status.UpdatedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        AddEvent("Client", $"{status.ClientState} - {status.ClientMessage}");
        AddEvent("Server", $"{status.ServerState} - {status.ServerMessage}");

        if (!string.IsNullOrWhiteSpace(status.LastError))
        {
            AddEvent("Ошибка", LastError, "Error", status.LastError);
        }
    }

    private void ApplySnapshot(ModbusSnapshot snapshot)
    {
        if (snapshot.Role == ModbusRuntimeRole.Client)
        {
            ApplySnapshotToRows(snapshot, ClientCoils, ClientRegisters, ClientTypedRegisters);
            ClientLastUpdated = FormatTimestamp(snapshot.Timestamp);
            return;
        }

        if (snapshot.Role == ModbusRuntimeRole.Server)
        {
            ApplySnapshotToRows(snapshot, ServerCoils, ServerRegisters, ServerTypedRegisters);
            ServerLastUpdated = FormatTimestamp(snapshot.Timestamp);
        }
    }

    private void ApplySnapshotToRows(
        ModbusSnapshot snapshot,
        ObservableCollection<ModbusCoilRow> coils,
        ObservableCollection<ModbusRegisterRow> registers,
        ObservableCollection<ModbusTypedRegisterRow> typedRegisters)
    {
        for (var index = 0; index < snapshot.Coils.Count && index < coils.Count; index++)
        {
            coils[index].SetFromSnapshot(snapshot.Coils[index]);
        }

        for (var index = 0; index < snapshot.HoldingRegisters.Count && index < registers.Count; index++)
        {
            registers[index].SetFromSnapshot(snapshot.HoldingRegisters[index]);
        }

        foreach (var typedRegister in typedRegisters)
        {
            typedRegister.SetFromSnapshot(snapshot.HoldingRegisters);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.DecodedRegisters.Error))
        {
            SetLastError($"{snapshot.Role}: {snapshot.DecodedRegisters.Error}");
            AddEvent("Decode", LastError, "Warning", snapshot.DecodedRegisters.Error);
        }
    }

    private async Task WriteClientCoilAsync(int address, bool value)
    {
        if (!ClientCoilsEnabled)
        {
            ReportInputError("Coils клиента отключены в настройках.");
            return;
        }

        await WriteAsync(
            $"Client Coil[{address}]={value}",
            () => _clientService.WriteCoilAsync(address, value));
    }

    private async Task WriteClientRegisterAsync(int address, ushort value)
    {
        if (!ClientHoldingRegistersEnabled)
        {
            ReportInputError("Holding Registers клиента отключены в настройках.");
            return;
        }

        await WriteAsync(
            $"Client Register[{address}]={value}",
            () => _clientService.WriteRegisterAsync(address, value));
    }

    private async Task WriteClientRegistersAsync(int address, ushort[] values)
    {
        if (!ClientHoldingRegistersEnabled)
        {
            ReportInputError("Holding Registers клиента отключены в настройках.");
            return;
        }

        await WriteAsync(
            $"Client Registers[{address}..{address + values.Length - 1}]",
            () => _clientService.WriteRegistersAsync(address, values));
    }

    private async Task WriteServerCoilAsync(int address, bool value)
    {
        await WriteAsync(
            $"Server Coil[{address}]={value}",
            () => _serverService.SetCoilAsync(address, value));
    }

    private async Task WriteServerRegisterAsync(int address, ushort value)
    {
        await WriteAsync(
            $"Server Register[{address}]={value}",
            () => _serverService.SetRegisterAsync(address, value));
    }

    private async Task WriteServerRegistersAsync(int address, ushort[] values)
    {
        await WriteAsync(
            $"Server Registers[{address}..{address + values.Length - 1}]",
            () => _serverService.SetRegistersAsync(address, values));
    }

    private async Task WriteAsync(string eventText, Func<Task> action)
    {
        try
        {
            await action();
            AddEvent("UI", eventText);
        }
        catch (Exception ex)
        {
            SetLastError(ex.Message);
            AddEvent("Ошибка записи", LastError, "Error", ex.ToString());
        }
    }

    private void RebuildAllRowsFromSettings()
    {
        RebuildClientRowsFromSettings();
        RebuildServerRowsFromSettings();
    }

    private void RebuildClientRowsFromSettings()
    {
        RebuildRows(
            ClientCoils,
            ClientRegisters,
            ClientTypedRegisters,
            ClientCoilsEnabled ? Clamp(ClientCoilCount, 0, 2000) : 0,
            ClientHoldingRegistersEnabled ? Clamp(ClientRegisterCount, 0, 123) : 0,
            WriteClientCoilAsync,
            WriteClientRegisterAsync,
            WriteClientRegistersAsync,
            ReportInputError);
    }

    private void RebuildServerRowsFromSettings()
    {
        RebuildRows(
            ServerCoils,
            ServerRegisters,
            ServerTypedRegisters,
            Clamp(ServerCoilCount, 0, 2000),
            Clamp(ServerRegisterCount, 0, 123),
            WriteServerCoilAsync,
            WriteServerRegisterAsync,
            WriteServerRegistersAsync,
            ReportInputError);
    }

    private static void RebuildRows(
        ObservableCollection<ModbusCoilRow> coils,
        ObservableCollection<ModbusRegisterRow> registers,
        ObservableCollection<ModbusTypedRegisterRow> typedRegisters,
        int coilCount,
        int registerCount,
        Func<int, bool, Task> writeCoilAsync,
        Func<int, ushort, Task> writeRegisterAsync,
        Func<int, ushort[], Task> writeRegistersAsync,
        Action<string> reportInputError)
    {
        coils.Clear();
        registers.Clear();
        typedRegisters.Clear();

        for (var address = 0; address < coilCount; address++)
        {
            coils.Add(new ModbusCoilRow(address, writeCoilAsync));
        }

        for (var address = 0; address < registerCount; address++)
        {
            registers.Add(new ModbusRegisterRow(address, writeRegisterAsync));
        }

        AddTypedRow(typedRegisters, registerCount, "INT [0]", 0, 1, ModbusRegisterValueKind.Int, writeRegistersAsync, reportInputError);
        AddTypedRow(typedRegisters, registerCount, "INT [1]", 1, 1, ModbusRegisterValueKind.Int, writeRegistersAsync, reportInputError);
        AddTypedRow(typedRegisters, registerCount, "REAL [2..3]", 2, 2, ModbusRegisterValueKind.Real, writeRegistersAsync, reportInputError);
        AddTypedRow(typedRegisters, registerCount, "STRING [4..6]", 4, 3, ModbusRegisterValueKind.String, writeRegistersAsync, reportInputError);
        AddTypedRow(typedRegisters, registerCount, "DATE [10..11]", 10, 2, ModbusRegisterValueKind.Date, writeRegistersAsync, reportInputError);
        AddTypedRow(typedRegisters, registerCount, "DWORD [12..13]", 12, 2, ModbusRegisterValueKind.Dword, writeRegistersAsync, reportInputError);
    }

    private static void AddTypedRow(
        ObservableCollection<ModbusTypedRegisterRow> rows,
        int registerCount,
        string label,
        int address,
        int count,
        ModbusRegisterValueKind kind,
        Func<int, ushort[], Task> writeRegistersAsync,
        Action<string> reportInputError)
    {
        if (address + count <= registerCount)
        {
            rows.Add(new ModbusTypedRegisterRow(label, address, count, kind, writeRegistersAsync, reportInputError));
        }
    }

    private void ReportInputError(string message)
    {
        SetLastError(message);
        AddEvent("Ошибка ввода", LastError, "Error", message);
    }

    private void AddEvent(string source, string message, string level = "Info", string details = "")
    {
        var now = DateTimeOffset.Now;
        var shortMessage = TrimForUi(message);
        var key = $"{source}|{level}|{shortMessage}";

        if (_eventTimes.TryGetValue(key, out var lastEventAt)
            && now - lastEventAt < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _eventTimes[key] = now;
        Events.Insert(0, new ModbusEventRow(now, source, level, shortMessage, details));

        while (Events.Count > 200)
        {
            Events.RemoveAt(Events.Count - 1);
        }
    }

    private void SetLastError(string? message)
    {
        LastError = TrimForUi(message ?? string.Empty);
    }

    private void RaiseCommandStateChanged()
    {
        this.RaisePropertyChanged(nameof(CanStartClient));
        this.RaisePropertyChanged(nameof(CanStopClient));
        this.RaisePropertyChanged(nameof(CanRestartClient));
        this.RaisePropertyChanged(nameof(CanStartServer));
        this.RaisePropertyChanged(nameof(CanStopServer));
        this.RaisePropertyChanged(nameof(CanRestartServer));
        this.RaisePropertyChanged(nameof(CanStartBoth));
        this.RaisePropertyChanged(nameof(CanStopBoth));
        this.RaisePropertyChanged(nameof(CanRestartBoth));
        this.RaisePropertyChanged(nameof(CanOpenSettings));
        this.RaisePropertyChanged(nameof(CanEditClientValues));
        this.RaisePropertyChanged(nameof(CanEditClientCoils));
        this.RaisePropertyChanged(nameof(CanEditClientRegisters));
        this.RaisePropertyChanged(nameof(CanEditServerValues));
    }

    private static ModbusConnectionState ParseState(string value)
    {
        return Enum.TryParse<ModbusConnectionState>(value, out var state)
            ? state
            : ModbusConnectionState.Stopped;
    }

    private static bool IsStartable(ModbusConnectionState state)
    {
        return state is ModbusConnectionState.Stopped or ModbusConnectionState.Faulted;
    }

    private static bool IsConfigurable(ModbusConnectionState state)
    {
        return state is ModbusConnectionState.Stopped or ModbusConnectionState.Faulted;
    }

    private static bool IsStoppable(ModbusConnectionState state)
    {
        return state is ModbusConnectionState.Starting or ModbusConnectionState.Reconnecting or ModbusConnectionState.Running;
    }

    private static string NormalizeAddress(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "127.0.0.1" : value.Trim();
    }

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        return timestamp == default
            ? "-"
            : timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string TrimForUi(string message)
    {
        const int maxLength = 180;

        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var singleLine = message.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= maxLength
            ? singleLine
            : string.Concat(singleLine.AsSpan(0, maxLength), "...");
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(Math.Max(value, min), max);
    }
}

/// <summary>
/// Короткая строка журнала Modbus с подробностями для tooltip.
/// </summary>
public sealed class ModbusEventRow
{
    /// <summary>
    /// Создает строку события.
    /// </summary>
    public ModbusEventRow(DateTimeOffset timestamp, string source, string level, string message, string details)
    {
        Timestamp = timestamp;
        Source = source;
        Level = level;
        Message = message;
        Details = string.IsNullOrWhiteSpace(details) ? message : details;
    }

    /// <summary>
    /// Время события.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Источник события: Client, Server, UI или ошибка.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Уровень события.
    /// </summary>
    public string Level { get; }

    /// <summary>
    /// Короткое сообщение для отображения.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Полные детали события.
    /// </summary>
    public string Details { get; }

    /// <summary>
    /// Время в формате журнала.
    /// </summary>
    public string TimeText => Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
}
