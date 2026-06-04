using Avalonia.Threading;
using Configurator.Application.Services;
using Configurator.Application.Services.Dialogs;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Configurator.Desktop.Workspace.ModbusDemo;

/// <summary>
/// Экран проекта ModbusDemo: связывает элементы управления с Holding Registers удаленного устройства.
/// </summary>
public sealed class ModbusDemoViewModel : ViewModelBase, IDisposable
{
    private const int HoldingRegisterBaseAddress = 16384;

    private readonly IModbusDemoTcpService _modbusTcpService;
    private readonly IModbusDemoOptionsProvider _optionsProvider;
    private readonly IDialogService _dialogService;
    private readonly IAppConfigService _appConfigService;
    private readonly Action<Action> _dispatchToUi;
    private readonly Dictionary<string, ModbusTelemetryRegisterGroup> _telemetryByPoint = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModbusParameterRow> _parametersByPoint = new(StringComparer.OrdinalIgnoreCase);
    // Команды ModbusDemo отправляются целыми Holding Register, поэтому UI хранит последнее слово
    // каждого командного регистра и меняет в нем только выбранный бит.
    private readonly Dictionary<string, ushort> _commandWords = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IDisposable> _dataSubscriptions = [];
    private readonly object _lifecycleSync = new();
    private readonly SemaphoreSlim _commandWriteGate = new(1, 1);
    private CancellationTokenSource? _lifecycleCancellation;
    private int _activeLifecycleOperationCount;
    private bool _isCommandRunning;
    private bool _isLifecycleOperationActive;
    private bool _isWaitingForConnection;
    private ModbusConnectionState _clientState = ModbusConnectionState.Stopped;
    private ModbusConnectionState _serverState = ModbusConnectionState.Stopped;
    private ModbusOptions _currentOptions;
    private string _lastError = string.Empty;
    private string _statusText = "Modbus stopped.";

    /// <summary>
    /// Создает модель экрана и подписывается на readable-точки карты ModbusDemo.
    /// </summary>
    public ModbusDemoViewModel(
        IModbusDemoTcpService modbusTcpService,
        IModbusDemoOptionsProvider optionsProvider,
        IDialogService dialogService,
        IAppConfigService appConfigService)
        : this(modbusTcpService, optionsProvider, dialogService, appConfigService, DispatchToUi)
    {
    }

    internal ModbusDemoViewModel(
        IModbusDemoTcpService modbusTcpService,
        IModbusDemoOptionsProvider optionsProvider,
        IDialogService dialogService,
        IAppConfigService appConfigService,
        Action<Action> dispatchToUi)
    {
        _modbusTcpService = modbusTcpService;
        _optionsProvider = optionsProvider;
        _dialogService = dialogService;
        _appConfigService = appConfigService;
        ArgumentNullException.ThrowIfNull(dispatchToUi);
        _dispatchToUi = dispatchToUi;
        _currentOptions = _optionsProvider.CurrentValue.Clone();

        TelemetryGroups = CreateTelemetryGroups();
        CommandGroups = CreateCommandGroups();
        ParameterRows = CreateParameterRows();

        foreach (var group in TelemetryGroups)
        {
            _telemetryByPoint[group.PointName] = group;
        }

        foreach (var group in CommandGroups)
        {
            _commandWords[group.PointName] = 0;
        }

        foreach (var parameter in ParameterRows)
        {
            _parametersByPoint[parameter.PointName] = parameter;
        }

        var canStartServer = this.WhenAnyValue(x => x.CanStartServer);
        var canStartClient = this.WhenAnyValue(x => x.CanStartClient);
        var canStop = this.WhenAnyValue(x => x.CanStop);
        var canOpenSettings = this.WhenAnyValue(x => x.CanOpenSettings);
        var canRunCommand = this.WhenAnyValue(x => x.IsCommandRunning).Select(isRunning => !isRunning);
        StartServerCommand = ReactiveCommand.Create(
            () => StartModbusLifecycleOperation(
                "StartServer",
                (options, ct) => _modbusTcpService.StartServerAsync(options, ct)),
            canStartServer);
        StartClientCommand = ReactiveCommand.Create(
            () => StartModbusLifecycleOperation(
                "StartClient",
                (options, ct) => _modbusTcpService.StartClientAsync(options, ct)),
            canStartClient);
        StopCommand = ReactiveCommand.Create(
            StopModbusLifecycleOperation,
            canStop);
        OpenSettingsCommand = ReactiveCommand.CreateFromTask(OpenSettingsAsync, canOpenSettings);
        ReadParametersCommand = ReactiveCommand.CreateFromTask(
            () => RunUiCommandAsync(ReadParametersCoreAsync),
            canRunCommand);
        WriteParametersCommand = ReactiveCommand.CreateFromTask(
            () => RunUiCommandAsync(WriteParametersCoreAsync),
            canRunCommand);

        _modbusTcpService.StateChanged += OnStateChanged;
        ApplyState(_modbusTcpService.State);

        foreach (var pointName in _telemetryByPoint.Keys.Concat(_parametersByPoint.Keys))
        {
            _dataSubscriptions.Add(_modbusTcpService.Subscribe(pointName, OnDataValueChanged));
        }
    }

    /// <summary>
    /// Группы телеметрических регистров и их подписанные биты.
    /// </summary>
    public ObservableCollection<ModbusTelemetryRegisterGroup> TelemetryGroups { get; }

    /// <summary>
    /// Группы командных регистров, которые UI пишет как целые UInt16-слова.
    /// </summary>
    public ObservableCollection<ModbusCommandRegisterGroup> CommandGroups { get; }

    /// <summary>
    /// Изменяемые числовые параметры устройства.
    /// </summary>
    public ObservableCollection<ModbusParameterRow> ParameterRows { get; }

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
    /// Копирует последние прочитанные значения параметров в поля редактирования.
    /// </summary>
    public ReactiveCommand<Unit, Unit> ReadParametersCommand { get; }

    /// <summary>
    /// Записывает отредактированные значения параметров в Holding Registers.
    /// </summary>
    public ReactiveCommand<Unit, Unit> WriteParametersCommand { get; }

    /// <summary>
    /// Показывает, что фасад запускается, останавливается или выполняет UI-запись.
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
    /// Показывает, что Start/Stop выполняется в фоне и UI-команда уже отпущена.
    /// </summary>
    public bool IsLifecycleOperationActive
    {
        get => _isLifecycleOperationActive;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isLifecycleOperationActive, value);
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
    /// Показывает, что клиентская роль ожидает подключения.
    /// </summary>
    public bool IsWaitingForConnection
    {
        get => _isWaitingForConnection;
        private set => this.RaiseAndSetIfChanged(ref _isWaitingForConnection, value);
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
    public bool CanStop => IsLifecycleOperationActive || (!IsCommandRunning && (IsStoppable(_clientState) || IsStoppable(_serverState)));

    /// <summary>
    /// Можно ли открыть настройки demo-стека без изменения активного подключения.
    /// </summary>
    public bool CanOpenSettings => !IsCommandRunning && IsConfigurable(_clientState) && IsConfigurable(_serverState);

    /// <summary>
    /// Освобождает подписки фасада и синхронизационные ресурсы.
    /// </summary>
    public void Dispose()
    {
        CancelActiveLifecycleOperation();
        _modbusTcpService.StateChanged -= OnStateChanged;

        foreach (var subscription in _dataSubscriptions)
        {
            subscription.Dispose();
        }

        _commandWriteGate.Dispose();
    }

    internal async Task<bool> WriteCommandBitAsync(ModbusCommandBitRow row, bool value)
    {
        await _commandWriteGate.WaitAsync();

        ushort previousWord;
        ushort nextWord;

        try
        {
            previousWord = _commandWords.TryGetValue(row.PointName, out var currentWord)
                ? currentWord
                : (ushort)0;
            var mask = checked((ushort)(1 << row.BitIndex));

            // SetAsync принимает значение всего регистра. При ошибке ниже откатываем локальное слово,
            // чтобы следующее переключение не унаследовало бит, который фактически не ушел в Modbus.
            nextWord = value
                ? (ushort)(previousWord | mask)
                : (ushort)(previousWord & ~mask);
            _commandWords[row.PointName] = nextWord;
        }
        finally
        {
            _commandWriteGate.Release();
        }

        var result = await _modbusTcpService.SetAsync(row.PointName, nextWord);
        if (result.Succeeded)
        {
            return true;
        }

        await _commandWriteGate.WaitAsync();

        try
        {
            _commandWords[row.PointName] = previousWord;
        }
        finally
        {
            _commandWriteGate.Release();
        }

        await ShowErrorAsync(OperationMessage(result));
        return false;
    }

    private Task ReadParametersCoreAsync()
    {
        var missingCount = 0;

        // Polling обновляет только LastReadValueText. В поля редактирования значения попадают
        // по явной команде, чтобы пользовательский ввод не затирался очередным snapshot.
        foreach (var parameter in ParameterRows)
        {
            if (!parameter.CopyLatestToEdit())
            {
                missingCount++;
            }
        }

        LastError = missingCount == 0
            ? string.Empty
            : $"Нет прочитанных значений для параметров: {missingCount}.";

        return Task.CompletedTask;
    }

    private async Task WriteParametersCoreAsync()
    {
        foreach (var parameter in ParameterRows)
        {
            if (!parameter.TryGetEditValue(out _))
            {
                await ShowErrorAsync("Параметры должны быть числами от 0 до 65535.");
                return;
            }
        }

        foreach (var parameter in ParameterRows)
        {
            parameter.TryGetEditValue(out var value);
            var result = await _modbusTcpService.SetAsync(parameter.PointName, value);
            if (!result.Succeeded)
            {
                await ShowErrorAsync(OperationMessage(result));
                return;
            }
        }

        LastError = string.Empty;
    }

    private void StartModbusLifecycleOperation(
        string operationName,
        Func<ModbusOptions, CancellationToken, Task<ModbusOperationResult>> action)
    {
        var options = BuildOptions();
        var cts = new CancellationTokenSource();
        SetActiveLifecycleCancellation(cts);
        BeginLifecycleOperation();
        System.Diagnostics.Debug.WriteLine($"ModbusDemo {operationName} requested.");

        // Lifecycle-фасад может синхронно валидировать карту, останавливать встречную роль
        // и начинать TCP-подключение; UI-команда только запускает фон и сразу отпускает экран.
        _ = Task.Run(() => RunModbusLifecycleOperationAsync(
            operationName,
            ct => action(options, ct),
            cts));
    }

    private void StopModbusLifecycleOperation()
    {
        // Stop должен быть доступен даже во время Starting/Reconnecting: он отменяет
        // текущий lifecycle и отдельно просит фасад освободить роли Modbus.
        CancelActiveLifecycleOperation();
        var cts = new CancellationTokenSource();
        SetActiveLifecycleCancellation(cts);
        BeginLifecycleOperation();
        System.Diagnostics.Debug.WriteLine("ModbusDemo Stop requested.");

        _ = Task.Run(() => RunModbusLifecycleOperationAsync(
            "Stop",
            ct => _modbusTcpService.StopAsync(ct),
            cts));
    }

    private async Task RunModbusLifecycleOperationAsync(
        string operationName,
        Func<CancellationToken, Task<ModbusOperationResult>> action,
        CancellationTokenSource cts)
    {
        var ct = cts.Token;
        System.Diagnostics.Debug.WriteLine($"ModbusDemo {operationName} background started.");

        try
        {
            var result = await action(ct);
            if (!result.Succeeded && !ct.IsCancellationRequested)
            {
                DispatchError(OperationMessage(result));
            }

            System.Diagnostics.Debug.WriteLine($"ModbusDemo {operationName} completed.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            System.Diagnostics.Debug.WriteLine($"ModbusDemo {operationName} canceled.");
        }
        catch (Exception ex)
        {
            DispatchError($"Операция ModbusDemo {operationName} не выполнена. {ex.Message}");
        }
        finally
        {
            ClearActiveLifecycleCancellation(cts);
            _dispatchToUi(EndLifecycleOperation);
        }
    }

    private void BeginLifecycleOperation()
    {
        _activeLifecycleOperationCount++;
        IsLifecycleOperationActive = true;
        IsCommandRunning = true;
    }

    private void EndLifecycleOperation()
    {
        _activeLifecycleOperationCount = Math.Max(0, _activeLifecycleOperationCount - 1);
        IsLifecycleOperationActive = _activeLifecycleOperationCount > 0;
        IsCommandRunning = IsLifecycleOperationActive;
    }

    private void SetActiveLifecycleCancellation(CancellationTokenSource cts)
    {
        lock (_lifecycleSync)
        {
            _lifecycleCancellation = cts;
        }
    }

    private void CancelActiveLifecycleOperation()
    {
        lock (_lifecycleSync)
        {
            _lifecycleCancellation?.Cancel();
        }
    }

    private void ClearActiveLifecycleCancellation(CancellationTokenSource cts)
    {
        lock (_lifecycleSync)
        {
            if (ReferenceEquals(_lifecycleCancellation, cts))
            {
                _lifecycleCancellation = null;
            }
        }

        cts.Dispose();
    }

    private void DispatchError(string message)
    {
        _dispatchToUi(() => _ = ShowErrorAsync(message));
    }

    private async Task RunUiCommandAsync(Func<Task> action)
    {
        IsCommandRunning = true;

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync($"Операция ModbusDemo не выполнена. {ex.Message}");
        }
        finally
        {
            IsCommandRunning = false;
        }
    }

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

    private void OnStateChanged(object? sender, ModbusServiceState state)
    {
        // Клиентский polling работает в фоновой задаче. Все изменения bound-свойств
        // ModbusDemo должны возвращаться на UI-поток Avalonia, иначе экран может зависнуть.
        _dispatchToUi(() => ApplyState(state));
    }

    private void OnDataValueChanged(ModbusDataValue value)
    {
        // Snapshot-ы клиента тоже приходят с фонового потока; коллекции и строки UI
        // обновляем только через dispatcher, как в основном Modbus-экране.
        _dispatchToUi(() => ApplyDataValue(value));
    }

    private void ApplyState(ModbusServiceState state)
    {
        _clientState = state.ClientState;
        _serverState = state.ServerState;
        StatusText = $"{state.ActiveRole}: {state.Message}";
        IsWaitingForConnection = state.IsWaitingForConnection;
        LastError = state.LastError ?? string.Empty;
        RaiseCommandStateChanged();
    }

    private void ApplyDataValue(ModbusDataValue value)
    {
        if (!TryReadUInt16(value.Value, out var registerValue))
        {
            return;
        }

        if (_telemetryByPoint.TryGetValue(value.Name, out var telemetry))
        {
            telemetry.ApplyRegisterValue(registerValue);
            return;
        }

        if (_parametersByPoint.TryGetValue(value.Name, out var parameter))
        {
            parameter.ApplyReadValue(registerValue);
        }
    }

    private async Task ShowErrorAsync(string message)
    {
        LastError = message;
        await _dialogService.ShowErrorAsync("Modbus TCP", message);
    }

    private static string OperationMessage(ModbusOperationResult result)
        => result.ErrorDetails is { Length: > 0 } details
            ? $"{result.ErrorMessage ?? "Modbus operation failed."} {details}"
            : result.ErrorMessage ?? "Modbus operation failed.";

    private void RaiseCommandStateChanged()
    {
        this.RaisePropertyChanged(nameof(CanStartClient));
        this.RaisePropertyChanged(nameof(CanStartServer));
        this.RaisePropertyChanged(nameof(CanStop));
        this.RaisePropertyChanged(nameof(CanOpenSettings));
    }

    private static ObservableCollection<ModbusTelemetryRegisterGroup> CreateTelemetryGroups()
        =>
        [
            new("Telemetry_1", 16384,
            [
                "Д_КЮБЕЛЬ_ОТКРЫТ",
                "Д_КЮБЕЛЬ_ЗАКРЫТ",
                "КЮБЕЛЬ_ВПЕРЕД",
                "КЮБЕЛЬ_НАЗАД",
                "КЮБЕЛЬ_ОТКРЫТИЕ",
                "КЮБЕЛЬ_ЗАКРЫТИЕ",
                "Д_ПОЗИЦИЯ_1",
                "Д_ПОЗИЦИЯ_2",
                "Д_ПОЗИЦИЯ_3",
                "Д_ПОЗИЦИЯ_4",
                "РУЧНОЕ_УПРАВЛЕНИЕ",
                "Д_БЛОК_ВПЕРЕД",
                "Д_БЛОК_НАЗАД",
                "АВАРИЯ_m",
                "АВАРИЯ_ТОРМОЗА",
                "АВАРИЯ_1М"
            ],
            redIndicatorBitIndex: 0),
            new("Telemetry_2", 16385,
            [
                "B_ВПЕРЕД",
                "B_НАЗАД",
                "B_СКОРОСТЬ_1",
                "B_СКОРОСТЬ_2",
                "B_КЮБЕЛЬ_ОТКРЫТЬ",
                "B_КЮБЕЛЬ_ЗАКРЫТЬ",
                "B_ПУСК_СБРОС",
                "B_СВЕТЗВУК"
            ]),
            new("Telemetry_3", 16386,
            [
                "T_Д_ТРИГ",
                "T_АВАРИЯ",
                "T_АВАРИЯ-ПОЗ_УПРАВ",
                "T_АВАРИЯ-ВРАЩЕНИЕ",
                "T_АВАРИЯ-З_ВЫГРУЗКА",
                "T_АВАРИЯ-З_ЗАГРУЗКА",
                "ОБРЫВ"
            ]),
            new("Telemetry_4", 16387, [])
        ];

    private ObservableCollection<ModbusCommandRegisterGroup> CreateCommandGroups()
        =>
        [
            // Commands_* соответствуют write-only Holding Registers. Q1 визуально остается RadioButton,
            // остальные кнопочные команды работают как удерживаемые ToggleButton.
            new("Commands_1", 16388,
            [
                new("Commands_1", "C_СБРОС", 0, ModbusCommandControlKind.RadioButtonToggle, WriteCommandBitAsync),
                new("Commands_1", "C_ОТМЕНА", 1, ModbusCommandControlKind.CheckBox, WriteCommandBitAsync),
                new("Commands_1", "C_ПУСК-ВРАЩЕНИЕ", 2, ModbusCommandControlKind.ToggleButton, WriteCommandBitAsync),
                new("Commands_1", "C_ПУСК-З_ВЫГРУЗКА", 3, ModbusCommandControlKind.ToggleButton, WriteCommandBitAsync),
                new("Commands_1", "C_ВКЛ-З_ЗАГРУЗКА", 4, ModbusCommandControlKind.ToggleButton, WriteCommandBitAsync),
                new("Commands_1", "C_ПУСК-З_ЗАГРУЗКА", 5, ModbusCommandControlKind.ToggleButton, WriteCommandBitAsync)
            ]),
            new("Commands_2", 16389, []),
            new("Commands_3", 16390,
            [
                new("Commands_3", "СЕТЬ", 0, ModbusCommandControlKind.ToggleButton, WriteCommandBitAsync)
            ]),
            new("Commands_4", 16391,
            [
                new("Commands_4", "ПУСК", 0, ModbusCommandControlKind.ToggleButton, WriteCommandBitAsync),
                new("Commands_4", "СТОП", 1, ModbusCommandControlKind.ToggleButton, WriteCommandBitAsync)
            ])
        ];

    private static ObservableCollection<ModbusParameterRow> CreateParameterRows()
        =>
        [
            CreateParameter("MB_ТЕКУЩАЯ_ПОЗИЦИЯ", 16400, ModbusParameterEditorKind.Slider),
            CreateParameter("MB_N_АВАРИЯ-ПОЗ_УПРАВ", 16401),
            CreateParameter("MB_N_АВАРИЯ-ВРАЩЕНИЕ", 16402),
            CreateParameter("MB_СТАТУС-ВРАЩЕНИЕ", 16403),
            CreateParameter("MB_Hz", 16404),
            CreateParameter("MB_ЦЕЛЬ_ПОЗИЦИЯ", 16411),
            CreateParameter("MB_ВОЗВРАТ_ПОЗИЦИЯ", 16412),
            CreateParameter("MB_ТОП_СБРОС-ПОЗ_УПРАВ", 16413),
            CreateParameter("MB_ТОП_ФИЛЬТР-ПОЗ_УПРАВ", 16414),
            CreateParameter("MB_ТОП_АВАРИЯ-ПОЗ_УПРАВ", 16415),
            CreateParameter("MB_ТОП_ПАУЗА-ВРАЩЕНИЕ", 16416),
            CreateParameter("MB_ТОП_АВАРИЯ-ВРАЩЕНИЕ", 16417),
            CreateParameter("MB_ТОП_СБРОС-З_ВЫГРУЗКА", 16418),
            CreateParameter("MB_ТОП_СБРОС-З_ЗАГРУЗКА", 16419)
        ];

    private static ModbusParameterRow CreateParameter(
        string name,
        int absoluteAddress,
        ModbusParameterEditorKind editorKind = ModbusParameterEditorKind.TextBox)
        => new(name, name, absoluteAddress, editorKind);

    internal static int ToRegisterOffset(int absoluteAddress)
        => absoluteAddress - HoldingRegisterBaseAddress;

    private static bool TryReadUInt16(object? value, out ushort result)
    {
        try
        {
            result = Convert.ToUInt16(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            result = 0;
            return false;
        }
    }

    private static bool IsStartable(ModbusConnectionState state)
        => state is ModbusConnectionState.Stopped or ModbusConnectionState.Faulted;

    private static bool IsConfigurable(ModbusConnectionState state)
        => state is ModbusConnectionState.Stopped or ModbusConnectionState.Faulted;

    private static bool IsStoppable(ModbusConnectionState state)
        => state is ModbusConnectionState.Starting or ModbusConnectionState.Reconnecting or ModbusConnectionState.Running;

    private static void DispatchToUi(Action action)
    {
        Dispatcher.UIThread.Post(action);
    }
}

public sealed class ModbusTelemetryRegisterGroup : ViewModelBase
{
    private string _rawValueText = "0";

    public ModbusTelemetryRegisterGroup(
        string pointName,
        int absoluteAddress,
        IEnumerable<string> bitNames,
        int? redIndicatorBitIndex = null)
    {
        PointName = pointName;
        AbsoluteAddress = absoluteAddress;
        AddressText = absoluteAddress.ToString(CultureInfo.InvariantCulture);
        Bits = new ObservableCollection<ModbusTelemetryBitRow>(
            bitNames.Select((name, bitIndex) => new ModbusTelemetryBitRow(
                name,
                bitIndex,
                redIndicatorBitIndex == bitIndex)));
    }

    public string PointName { get; }

    public int AbsoluteAddress { get; }

    public string AddressText { get; }

    public bool HasBits => Bits.Count > 0;

    public ObservableCollection<ModbusTelemetryBitRow> Bits { get; }

    public string RawValueText
    {
        get => _rawValueText;
        private set => this.RaiseAndSetIfChanged(ref _rawValueText, value);
    }

    public void ApplyRegisterValue(ushort value)
    {
        RawValueText = value.ToString(CultureInfo.InvariantCulture);

        foreach (var bit in Bits)
        {
            bit.ApplyValue((value & (1 << bit.BitIndex)) != 0);
        }
    }
}

public sealed class ModbusTelemetryBitRow : ViewModelBase
{
    private bool _value;
    private bool _isRedIndicatorVisible;
    private string _valueText = "0";

    public ModbusTelemetryBitRow(string name, int bitIndex, bool hasRedIndicator = false)
    {
        Name = name;
        BitIndex = bitIndex;
        BitText = $"I{bitIndex + 1}";
        HasRedIndicator = hasRedIndicator;
    }

    public string Name { get; }

    public int BitIndex { get; }

    public string BitText { get; }

    public bool HasRedIndicator { get; }

    public bool Value
    {
        get => _value;
        private set => this.RaiseAndSetIfChanged(ref _value, value);
    }

    public string ValueText
    {
        get => _valueText;
        private set => this.RaiseAndSetIfChanged(ref _valueText, value);
    }

    public bool IsRedIndicatorVisible
    {
        get => _isRedIndicatorVisible;
        private set => this.RaiseAndSetIfChanged(ref _isRedIndicatorVisible, value);
    }

    public void ApplyValue(bool value)
    {
        Value = value;
        ValueText = value ? "1" : "0";
        IsRedIndicatorVisible = HasRedIndicator && value;
    }
}

public sealed class ModbusCommandRegisterGroup
{
    public ModbusCommandRegisterGroup(
        string pointName,
        int absoluteAddress,
        IEnumerable<ModbusCommandBitRow> rows)
    {
        PointName = pointName;
        AbsoluteAddress = absoluteAddress;
        AddressText = absoluteAddress.ToString(CultureInfo.InvariantCulture);
        Rows = new ObservableCollection<ModbusCommandBitRow>(rows);
    }

    public string PointName { get; }

    public int AbsoluteAddress { get; }

    public string AddressText { get; }

    public bool HasRows => Rows.Count > 0;

    public ObservableCollection<ModbusCommandBitRow> Rows { get; }
}

public sealed class ModbusCommandBitRow : ViewModelBase
{
    private readonly Func<ModbusCommandBitRow, bool, Task<bool>> _writeBitAsync;
    private bool _isChecked;
    private bool _isWriting;

    public ModbusCommandBitRow(
        string pointName,
        string name,
        int bitIndex,
        ModbusCommandControlKind controlKind,
        Func<ModbusCommandBitRow, bool, Task<bool>> writeBitAsync)
    {
        PointName = pointName;
        Name = name;
        BitIndex = bitIndex;
        ControlKind = controlKind;
        BitText = $"Q{bitIndex + 1}";
        _writeBitAsync = writeBitAsync;
    }

    public string PointName { get; }

    public string Name { get; }

    public int BitIndex { get; }

    public string BitText { get; }

    public ModbusCommandControlKind ControlKind { get; }

    public string RadioGroupName => $"{PointName}_Q{BitIndex + 1}";

    public bool IsToggleButton => ControlKind == ModbusCommandControlKind.ToggleButton;

    public bool IsRadioButtonToggle => ControlKind == ModbusCommandControlKind.RadioButtonToggle;

    public bool IsCheckBox => ControlKind == ModbusCommandControlKind.CheckBox;

    // В ModbusDemo больше нет импульсных press/release-команд: каждый command-контрол удерживает состояние.
    public bool IsHoldControl => ControlKind is
        ModbusCommandControlKind.ToggleButton
        or ModbusCommandControlKind.RadioButtonToggle
        or ModbusCommandControlKind.CheckBox;

    public bool IsWriting
    {
        get => _isWriting;
        private set => this.RaiseAndSetIfChanged(ref _isWriting, value);
    }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
            {
                return;
            }

            var previous = _isChecked;
            this.RaiseAndSetIfChanged(ref _isChecked, value);

            // Удерживаемые контролы пишут новое состояние сразу при изменении IsChecked.
            if (IsHoldControl)
            {
                _ = WriteHoldAsync(previous, value);
            }
        }
    }

    private async Task WriteHoldAsync(bool previous, bool value)
    {
        if (!await WriteAsync(value))
        {
            this.RaiseAndSetIfChanged(ref _isChecked, previous, nameof(IsChecked));
        }
    }

    private async Task<bool> WriteAsync(bool value)
    {
        IsWriting = true;

        try
        {
            return await _writeBitAsync(this, value);
        }
        finally
        {
            IsWriting = false;
        }
    }
}

public enum ModbusCommandControlKind
{
    ToggleButton,
    RadioButtonToggle,
    CheckBox
}

public sealed class ModbusParameterRow : ViewModelBase
{
    private string _editValueText = "0";
    private string _errorText = string.Empty;
    private bool _hasReadValue;
    private ushort _lastReadValue;
    private string _lastReadValueText = "—";
    private double _sliderValue;

    public ModbusParameterRow(
        string pointName,
        string name,
        int absoluteAddress,
        ModbusParameterEditorKind editorKind = ModbusParameterEditorKind.TextBox)
    {
        PointName = pointName;
        Name = name;
        AbsoluteAddress = absoluteAddress;
        AddressText = absoluteAddress.ToString(CultureInfo.InvariantCulture);
        EditorKind = editorKind;
    }

    public string PointName { get; }

    public string Name { get; }

    public int AbsoluteAddress { get; }

    public string AddressText { get; }

    public ModbusParameterEditorKind EditorKind { get; }

    public bool IsTextBox => EditorKind == ModbusParameterEditorKind.TextBox;

    public bool IsSlider => EditorKind == ModbusParameterEditorKind.Slider;

    public double SliderMinimum => 0;

    public double SliderMaximum => ushort.MaxValue;

    public string LastReadValueText
    {
        get => _lastReadValueText;
        private set => this.RaiseAndSetIfChanged(ref _lastReadValueText, value);
    }

    public string EditValueText
    {
        get => _editValueText;
        set
        {
            this.RaiseAndSetIfChanged(ref _editValueText, value);

            if (IsSlider && ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                this.RaiseAndSetIfChanged(ref _sliderValue, parsed, nameof(SliderValue));
            }
        }
    }

    public double SliderValue
    {
        get => _sliderValue;
        set
        {
            var rounded = Math.Clamp(Math.Round(value), SliderMinimum, SliderMaximum);
            this.RaiseAndSetIfChanged(ref _sliderValue, rounded);

            if (IsSlider)
            {
                EditValueText = ((ushort)rounded).ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    public string ErrorText
    {
        get => _errorText;
        private set => this.RaiseAndSetIfChanged(ref _errorText, value);
    }

    public void ApplyReadValue(ushort value)
    {
        _lastReadValue = value;
        _hasReadValue = true;
        LastReadValueText = value.ToString(CultureInfo.InvariantCulture);
        ErrorText = string.Empty;
        // Поле EditValueText намеренно не обновляется polling-ом: пользователь может редактировать
        // значение на ходу, а отправка должна происходить только по кнопке "Записать значения".
    }

    public bool CopyLatestToEdit()
    {
        if (!_hasReadValue)
        {
            ErrorText = "Нет данных";
            return false;
        }

        EditValueText = _lastReadValue.ToString(CultureInfo.InvariantCulture);
        ErrorText = string.Empty;
        return true;
    }

    public bool TryGetEditValue(out ushort value)
    {
        if (ushort.TryParse(EditValueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            ErrorText = string.Empty;
            return true;
        }

        ErrorText = "0..65535";
        return false;
    }
}

public enum ModbusParameterEditorKind
{
    TextBox,
    Slider
}
