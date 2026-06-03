using Configurator.Application.Services.Dialogs;
using Configurator.Application.Services;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using ReactiveUI;
using System;
using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace Configurator.Desktop.Workspace.ModbusDemo;

/// <summary>
/// Демонстрирует простые UI-привязки через высокоуровневый Modbus TCP фасад.
/// </summary>
public sealed class ModbusDemoViewModel : ViewModelBase, IDisposable
{
    private const string DemoButtonName = "DemoButton";
    private const string DemoInputName = "DemoInput";
    private const string DemoImageVisibleName = "DemoImageVisible";

    private readonly IModbusDemoTcpService _modbusTcpService;
    private readonly IModbusDemoOptionsProvider _optionsProvider;
    private readonly IDialogService _dialogService;
    private readonly IAppConfigService _appConfigService;
    private readonly IDisposable _buttonSubscription;
    private readonly IDisposable _inputSubscription;
    private readonly IDisposable _imageSubscription;
    private bool _demoButton;
    private bool _isCommandRunning;
    private bool _isDemoImageVisible;
    private bool _isWaitingForConnection;
    private bool _suppressButtonWrite;
    private ModbusConnectionState _clientState = ModbusConnectionState.Stopped;
    private ModbusConnectionState _serverState = ModbusConnectionState.Stopped;
    private ModbusOptions _currentOptions;
    private string _demoInputText = "0";
    private string _lastError = string.Empty;
    private string _statusText = "Modbus stopped.";

    /// <summary>
    /// Создает демо-модель представления и подписывается на точки данных фасада.
    /// </summary>
    public ModbusDemoViewModel(
        IModbusDemoTcpService modbusTcpService,
        IModbusDemoOptionsProvider optionsProvider,
        IDialogService dialogService,
        IAppConfigService appConfigService)
    {
        _modbusTcpService = modbusTcpService;
        _optionsProvider = optionsProvider;
        _dialogService = dialogService;
        _appConfigService = appConfigService;
        _currentOptions = _optionsProvider.CurrentValue.Clone();

        var canStartServer = this.WhenAnyValue(x => x.CanStartServer);
        var canStartClient = this.WhenAnyValue(x => x.CanStartClient);
        var canStop = this.WhenAnyValue(x => x.CanStop);
        var canOpenSettings = this.WhenAnyValue(x => x.CanOpenSettings);
        var canRunCommand = this.WhenAnyValue(x => x.IsCommandRunning).Select(isRunning => !isRunning);
        StartServerCommand = ReactiveCommand.CreateFromTask(
            () => RunCommandAsync(() => _modbusTcpService.StartServerAsync(BuildOptions())),
            canStartServer);
        StartClientCommand = ReactiveCommand.CreateFromTask(
            () => RunCommandAsync(() => _modbusTcpService.StartClientAsync(BuildOptions())),
            canStartClient);
        StopCommand = ReactiveCommand.CreateFromTask(
            () => RunCommandAsync(() => _modbusTcpService.StopAsync()),
            canStop);
        OpenSettingsCommand = ReactiveCommand.CreateFromTask(OpenSettingsAsync, canOpenSettings);
        SaveInputCommand = ReactiveCommand.CreateFromTask(SaveInputAsync, canRunCommand);

        _modbusTcpService.StateChanged += OnStateChanged;
        ApplyState(_modbusTcpService.State);

        _buttonSubscription = _modbusTcpService.Subscribe(DemoButtonName, ApplyDataValue);
        _inputSubscription = _modbusTcpService.Subscribe(DemoInputName, ApplyDataValue);
        _imageSubscription = _modbusTcpService.Subscribe(DemoImageVisibleName, ApplyDataValue);
    }

    /// <summary>
    /// Запускает серверную роль Modbus для демо-стека.
    /// </summary>
    public ReactiveCommand<Unit, Unit> StartServerCommand { get; }

    /// <summary>
    /// Запускает клиентскую роль Modbus для демо-стека.
    /// </summary>
    public ReactiveCommand<Unit, Unit> StartClientCommand { get; }

    /// <summary>
    /// Останавливает роли Modbus внутри демо-стека.
    /// </summary>
    public ReactiveCommand<Unit, Unit> StopCommand { get; }

    /// <summary>
    /// Открывает настройки независимой секции ModbusDemo.
    /// </summary>
    public ReactiveCommand<Unit, Unit> OpenSettingsCommand { get; }

    /// <summary>
    /// Записывает значение textbox в настроенный демо Holding Register.
    /// </summary>
    public ReactiveCommand<Unit, Unit> SaveInputCommand { get; }

    /// <summary>
    /// Значение toggle, которое пишет DemoButton при изменении пользователем.
    /// </summary>
    public bool DemoButton
    {
        get => _demoButton;
        set
        {
            if (_demoButton == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _demoButton, value);
            this.RaisePropertyChanged(nameof(DemoButtonText));

            if (!_suppressButtonWrite)
            {
                _ = WriteDemoButtonAsync(value);
            }
        }
    }

    /// <summary>
    /// Текст на toggle для Coil.
    /// </summary>
    public string DemoButtonText => DemoButton ? "Coil 0 = True" : "Coil 0 = False";

    /// <summary>
    /// Значение textbox для записи демо Holding Register.
    /// </summary>
    public string DemoInputText
    {
        get => _demoInputText;
        set => this.RaiseAndSetIfChanged(ref _demoInputText, value);
    }

    /// <summary>
    /// Показывает демо-картинку, когда DemoImageVisible равен 1.
    /// </summary>
    public bool IsDemoImageVisible
    {
        get => _isDemoImageVisible;
        private set => this.RaiseAndSetIfChanged(ref _isDemoImageVisible, value);
    }

    /// <summary>
    /// Показывает, что фасад запускается или переподключается.
    /// </summary>
    public bool IsWaitingForConnection
    {
        get => _isWaitingForConnection;
        private set => this.RaiseAndSetIfChanged(ref _isWaitingForConnection, value);
    }

    /// <summary>
    /// Не дает запускать несколько UI-команд одновременно.
    /// </summary>
    public bool IsCommandRunning
    {
        get => _isCommandRunning;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isCommandRunning, value);
            RaiseCommandStateChanged();
        }
    }

    /// <summary>
    /// Текущий статус фасада в заголовке демо-экрана.
    /// </summary>
    public string StatusText
    {
        get => _statusText;
        private set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    /// <summary>
    /// Последняя ошибка операции, показанная демо-экраном.
    /// </summary>
    public string LastError
    {
        get => _lastError;
        private set => this.RaiseAndSetIfChanged(ref _lastError, value);
    }

    /// <summary>
    /// Можно ли запускать демо-клиент.
    /// </summary>
    public bool CanStartClient => !IsCommandRunning && IsStartable(_clientState);

    /// <summary>
    /// Можно ли запускать демо-сервер.
    /// </summary>
    public bool CanStartServer => !IsCommandRunning && IsStartable(_serverState);

    /// <summary>
    /// Можно ли останавливать демо-стек.
    /// </summary>
    public bool CanStop => !IsCommandRunning && (IsStoppable(_clientState) || IsStoppable(_serverState));

    /// <summary>
    /// Можно ли открыть настройки demo-стека без изменения активного подключения.
    /// </summary>
    public bool CanOpenSettings => !IsCommandRunning && IsConfigurable(_clientState) && IsConfigurable(_serverState);

    /// <summary>
    /// Освобождает подписки фасада.
    /// </summary>
    public void Dispose()
    {
        _modbusTcpService.StateChanged -= OnStateChanged;
        _buttonSubscription.Dispose();
        _inputSubscription.Dispose();
        _imageSubscription.Dispose();
    }

    /// <summary>
    /// Проверяет и записывает значение textbox в настроенный Holding Register.
    /// </summary>
    private async Task SaveInputAsync()
    {
        if (!ushort.TryParse(DemoInputText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            await ShowErrorAsync("Input must be a number from 0 to 65535.");
            return;
        }

        var result = await _modbusTcpService.SetAsync(DemoInputName, value);
        if (!result.Succeeded)
        {
            await ShowErrorAsync(OperationMessage(result));
        }
    }

    /// <summary>
    /// Записывает состояние toggle в настроенный Coil.
    /// </summary>
    private async Task WriteDemoButtonAsync(bool value)
    {
        var result = await _modbusTcpService.SetAsync(DemoButtonName, value);
        if (!result.Succeeded)
        {
            await ShowErrorAsync(OperationMessage(result));
        }
    }

    /// <summary>
    /// Выполняет команду фасада и отправляет ошибки в общий сервис диалогов.
    /// </summary>
    private async Task RunCommandAsync(Func<Task<ModbusOperationResult>> action)
    {
        IsCommandRunning = true;

        try
        {
            var result = await action();
            if (!result.Succeeded)
            {
                await ShowErrorAsync(OperationMessage(result));
            }
        }
        finally
        {
            IsCommandRunning = false;
        }
    }

    /// <summary>
    /// Клонирует текущие настройки, чтобы демо-команды не меняли общий объект конфигурации.
    /// </summary>
    /// <summary>
    /// Открывает редактор секции ModbusDemo и обновляет локальный кеш после сохранения.
    /// </summary>
    private async Task OpenSettingsAsync()
    {
        IsCommandRunning = true;

        try
        {
            var options = _appConfigService.GetSection<ModbusOptions>(ModbusOptions.DemoSectionName);
            var savedOptions = await _dialogService.EditModbusSettingsAsync(
                "Настройки Modbus TCP Demo",
                ModbusOptions.DemoSectionName,
                options);

            if (savedOptions is not null)
            {
                _currentOptions = savedOptions.Clone();
            }
        }
        catch (Exception ex)
        {
            await ShowErrorAsync($"Не удалось открыть или сохранить настройки. {ex.Message}");
        }
        finally
        {
            IsCommandRunning = false;
        }
    }

    private ModbusOptions BuildOptions()
        => _currentOptions.Clone();

    /// <summary>
    /// Получает изменения состояния фасада и переносит их в UI-свойства.
    /// </summary>
    private void OnStateChanged(object? sender, ModbusServiceState state)
    {
        ApplyState(state);
    }

    /// <summary>
    /// Преобразует состояние фасада в статус, флаг ожидания и текст последней ошибки.
    /// </summary>
    private void ApplyState(ModbusServiceState state)
    {
        _clientState = state.ClientState;
        _serverState = state.ServerState;
        StatusText = $"{state.ActiveRole}: {state.Message}";
        IsWaitingForConnection = state.IsWaitingForConnection;
        LastError = state.LastError ?? string.Empty;
        RaiseCommandStateChanged();
    }

    /// <summary>
    /// Применяет значения подписок без обратной записи в Modbus.
    /// </summary>
    private void ApplyDataValue(ModbusDataValue value)
    {
        switch (value.Name)
        {
            case DemoButtonName:
                _suppressButtonWrite = true;
                DemoButton = Convert.ToBoolean(value.Value, CultureInfo.InvariantCulture);
                _suppressButtonWrite = false;
                break;
            case DemoInputName:
                DemoInputText = Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? "0";
                break;
            case DemoImageVisibleName:
                IsDemoImageVisible = Convert.ToUInt16(value.Value, CultureInfo.InvariantCulture) == 1;
                break;
        }
    }

    /// <summary>
    /// Показывает модальную ошибку и сохраняет ее на демо-экране.
    /// </summary>
    private async Task ShowErrorAsync(string message)
    {
        LastError = message;
        await _dialogService.ShowErrorAsync("Modbus TCP", message);
    }

    /// <summary>
    /// Собирает UI-текст из результата операции фасада.
    /// </summary>
    private static string OperationMessage(ModbusOperationResult result)
        => result.ErrorDetails is { Length: > 0 } details
            ? $"{result.ErrorMessage ?? "Modbus operation failed."} {details}"
            : result.ErrorMessage ?? "Modbus operation failed.";

    /// <summary>
    /// Обновляет состояние доступности команд демо-экрана.
    /// </summary>
    private void RaiseCommandStateChanged()
    {
        this.RaisePropertyChanged(nameof(CanStartClient));
        this.RaisePropertyChanged(nameof(CanStartServer));
        this.RaisePropertyChanged(nameof(CanStop));
        this.RaisePropertyChanged(nameof(CanOpenSettings));
    }

    /// <summary>
    /// Проверяет, можно ли запускать роль из текущего состояния.
    /// </summary>
    private static bool IsStartable(ModbusConnectionState state)
        => state is ModbusConnectionState.Stopped or ModbusConnectionState.Faulted;

    /// <summary>
    /// Проверяет, что роль не активна и ее параметры можно менять.
    /// </summary>
    private static bool IsConfigurable(ModbusConnectionState state)
        => state is ModbusConnectionState.Stopped or ModbusConnectionState.Faulted;

    /// <summary>
    /// Проверяет, можно ли останавливать роль из текущего состояния.
    /// </summary>
    private static bool IsStoppable(ModbusConnectionState state)
        => state is ModbusConnectionState.Starting or ModbusConnectionState.Reconnecting or ModbusConnectionState.Running;
}
