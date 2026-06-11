using Configurator.Application.Services;
using Configurator.Application.Services.Dialogs;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Desktop.Workspace.ModbusDemo;
using Avalonia.Controls;
using ReactiveUI.Builder;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed class ModbusDemoViewModelTests
{
    static ModbusDemoViewModelTests()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();
    }

    [Fact]
    public void TelemetrySubscriptionDecodesTelemetry1Bits()
    {
        var service = new FakeModbusTcpService();
        using var viewModel = CreateViewModel(service);
        var telemetry = viewModel.TelemetryGroups.Single(group => group.PointName == "Telemetry_1");

        service.Publish("Telemetry_1", (ushort)0xFFFF);

        Assert.Equal("65535", telemetry.RawValueText);
        Assert.All(telemetry.Bits, bit => Assert.True(bit.Value));
        Assert.All(telemetry.Bits, bit => Assert.Equal("1", bit.ValueText));

        service.Publish("Telemetry_1", (ushort)0);

        Assert.All(telemetry.Bits, bit => Assert.False(bit.Value));
        Assert.All(telemetry.Bits, bit => Assert.Equal("0", bit.ValueText));
    }

    [Fact]
    public void Telemetry1Bit1ControlsRedIndicator()
    {
        var service = new FakeModbusTcpService();
        using var viewModel = CreateViewModel(service);
        var bit = viewModel.TelemetryGroups
            .Single(group => group.PointName == "Telemetry_1")
            .Bits.Single(row => row.BitIndex == 0);

        service.Publish("Telemetry_1", (ushort)1);

        Assert.True(bit.HasRedIndicator);
        Assert.True(bit.IsRedIndicatorVisible);

        service.Publish("Telemetry_1", (ushort)0);

        Assert.False(bit.IsRedIndicatorVisible);
    }

    [Fact]
    public async Task MomentaryCommandWritesOneThenZero()
    {
        var service = new FakeModbusTcpService();
        var dialog = new FakeDialogService();
        using var viewModel = CreateViewModel(service, dialog);
        var command = FindCommand(viewModel, "Commands_1", 2);

        await command.PulseCommand.Execute().FirstAsync().ToTask();

        Assert.Equal(ModbusCommandControlKind.MomentaryButton, command.ControlKind);
        Assert.Equal(2, service.SetCalls.Count);
        Assert.Equal(("Commands_1", (ushort)4), service.SetCalls[0]);
        Assert.Equal(("Commands_1", (ushort)0), service.SetCalls[1]);
        Assert.Empty(dialog.Errors);
    }

    [Fact]
    public async Task PulseCommandDisablesWhileWriting()
    {
        var service = new FakeModbusTcpService();
        using var viewModel = CreateViewModel(service);
        var command = FindCommand(viewModel, "Commands_1", 2);
        var setStarted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSet = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var canExecuteValues = new List<bool>();

        service.SetCallStarted = setStarted;
        service.SetCallRelease = releaseSet;

        using var subscription = command.PulseCommand.CanExecute.Subscribe(canExecuteValues.Add);

        var pulseTask = command.PulseCommand.Execute().FirstAsync().ToTask();
        await setStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(command.IsWriting);
        Assert.True(await WaitForAsync(() => canExecuteValues.Count > 0 && !canExecuteValues.Last()));
        Assert.Single(service.SetCalls);

        releaseSet.SetResult(true);
        await pulseTask;

        Assert.False(command.IsWriting);
        Assert.Equal(2, service.SetCalls.Count);
    }

    [Fact]
    public async Task CommandBitWritesAreSerializedThroughModbusSet()
    {
        var service = new FakeModbusTcpService();
        using var viewModel = CreateViewModel(service);
        var firstCommand = FindCommand(viewModel, "Commands_1", 2);
        var secondCommand = FindCommand(viewModel, "Commands_1", 3);
        var firstSetStarted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstSet = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        service.SetCallStarted = firstSetStarted;
        service.SetCallRelease = releaseFirstSet;

        var firstWrite = viewModel.WriteCommandBitAsync(firstCommand, true);
        await firstSetStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var secondWrite = viewModel.WriteCommandBitAsync(secondCommand, true);
        await Task.Delay(100);

        Assert.Single(service.SetCalls);

        releaseFirstSet.SetResult(true);

        Assert.True(await firstWrite);
        Assert.True(await secondWrite);
        Assert.Equal(2, service.SetCalls.Count);
        Assert.Equal(("Commands_1", (ushort)4), service.SetCalls[0]);
        Assert.Equal(("Commands_1", (ushort)12), service.SetCalls[1]);
    }

    [Fact]
    public async Task PulseCommandAttemptsResetAndReportsResetFailure()
    {
        var service = new FakeModbusTcpService();
        var dialog = new FakeDialogService();
        using var viewModel = CreateViewModel(service, dialog);
        var command = FindCommand(viewModel, "Commands_1", 2);

        service.SetResults.Enqueue(ModbusOperationResult.Success());
        service.SetResults.Enqueue(ModbusOperationResult.Failure("WriteFailed", "Write failed.", "No ACK"));

        await command.PulseCommand.Execute().FirstAsync().ToTask();

        Assert.False(command.IsWriting);
        Assert.False(command.IsPulseActive);
        Assert.Equal(2, service.SetCalls.Count);
        Assert.Equal(("Commands_1", (ushort)4), service.SetCalls[0]);
        Assert.Equal(("Commands_1", (ushort)0), service.SetCalls[1]);
        var error = Assert.Single(dialog.Errors);
        Assert.Contains("Write failed.", error.Message, StringComparison.Ordinal);
        Assert.Contains("No ACK", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RadioButtonPulseCommandWritesOneThenZero()
    {
        var service = new FakeModbusTcpService();
        using var viewModel = CreateViewModel(service);
        var command = FindCommand(viewModel, "Commands_1", 0);

        await command.PressAsync();
        Assert.True(command.IsPulseActive);
        await command.ReleaseAsync();

        Assert.Equal(ModbusCommandControlKind.RadioButtonPulse, command.ControlKind);
        Assert.False(command.IsPulseActive);
        Assert.Equal(("Commands_1", (ushort)1), service.SetCalls[0]);
        Assert.Equal(("Commands_1", (ushort)0), service.SetCalls[1]);
    }

    [Fact]
    public async Task CheckBoxCommandWritesHeldState()
    {
        var service = new FakeModbusTcpService();
        using var viewModel = CreateViewModel(service);
        var command = FindCommand(viewModel, "Commands_1", 1);

        command.IsChecked = true;
        Assert.True(await WaitForAsync(() => service.SetCalls.Count == 1));
        command.IsChecked = false;
        Assert.True(await WaitForAsync(() => service.SetCalls.Count == 2));

        Assert.Equal(ModbusCommandControlKind.CheckBox, command.ControlKind);
        Assert.Equal(("Commands_1", (ushort)2), service.SetCalls[0]);
        Assert.Equal(("Commands_1", (ushort)0), service.SetCalls[1]);
    }

    [Fact]
    public async Task NetworkToggleWritesCommands3Bit()
    {
        var service = new FakeModbusTcpService();
        using var viewModel = CreateViewModel(service);
        var network = FindCommand(viewModel, "Commands_3", 0);

        network.IsChecked = true;
        Assert.True(await WaitForAsync(() => service.SetCalls.Count == 1));
        network.IsChecked = false;
        Assert.True(await WaitForAsync(() => service.SetCalls.Count == 2));

        Assert.Equal(ModbusCommandControlKind.ToggleButton, network.ControlKind);
        Assert.Equal(("Commands_3", (ushort)1), service.SetCalls[0]);
        Assert.Equal(("Commands_3", (ushort)0), service.SetCalls[1]);
    }

    [Fact]
    public async Task RadioButtonPulseBehaviorUsesControlDataContext()
    {
        var service = new FakeModbusTcpService();
        using var viewModel = CreateViewModel(service);
        var command = FindCommand(viewModel, "Commands_1", 0);
        var behavior = new ModbusDemoMomentaryCommandBehavior();
        var radioButton = new RadioButton { DataContext = command };

        var pressed = await behavior.PressAsync(radioButton);
        var released = await behavior.ReleaseAsync();

        Assert.True(pressed);
        Assert.True(released);
        Assert.Equal(("Commands_1", (ushort)1), service.SetCalls[0]);
        Assert.Equal(("Commands_1", (ushort)0), service.SetCalls[1]);
    }

    [Fact]
    public async Task ParameterReadCopiesLatestSnapshotWithoutPollingOverwritingEditText()
    {
        var service = new FakeModbusTcpService();
        using var viewModel = CreateViewModel(service);
        var parameter = viewModel.ParameterRows.Single(row => row.PointName == "MB_Hz");
        parameter.EditValueText = "77";

        service.Publish("MB_Hz", (ushort)123);

        Assert.Equal("123", parameter.LastReadValueText);
        Assert.Equal("77", parameter.EditValueText);

        await viewModel.ReadParametersCommand.Execute().FirstAsync().ToTask();

        Assert.Equal("123", parameter.EditValueText);
        Assert.Equal(string.Empty, parameter.ErrorText);
    }

    [Fact]
    public async Task WriteParametersWritesOnlyOnCommand()
    {
        var service = new FakeModbusTcpService();
        using var viewModel = CreateViewModel(service);

        foreach (var parameter in viewModel.ParameterRows)
        {
            parameter.EditValueText = "5";
        }

        await viewModel.WriteParametersCommand.Execute().FirstAsync().ToTask();

        Assert.Equal(viewModel.ParameterRows.Count, service.SetCalls.Count);
        Assert.All(service.SetCalls, call => Assert.Equal((ushort)5, call.Value));
        Assert.Equal(
            viewModel.ParameterRows.Select(row => row.PointName),
            service.SetCalls.Select(call => call.Name));
    }

    [Fact]
    public async Task SliderParameterWritesOnlyOnWriteCommand()
    {
        var service = new FakeModbusTcpService();
        using var viewModel = CreateViewModel(service);
        var sliderParameter = viewModel.ParameterRows.Single(row => row.AbsoluteAddress == 16400);

        sliderParameter.SliderValue = 321;

        Assert.True(sliderParameter.IsSlider);
        Assert.Equal("321", sliderParameter.EditValueText);
        Assert.Empty(service.SetCalls);

        await viewModel.WriteParametersCommand.Execute().FirstAsync().ToTask();

        Assert.Equal((sliderParameter.PointName, (ushort)321), service.SetCalls[0]);
    }

    [Fact]
    public async Task WriteParametersShowsErrorForInvalidValue()
    {
        var service = new FakeModbusTcpService();
        var dialog = new FakeDialogService();
        using var viewModel = CreateViewModel(service, dialog);
        viewModel.ParameterRows[0].EditValueText = "70000";

        await viewModel.WriteParametersCommand.Execute().FirstAsync().ToTask();

        Assert.Empty(service.SetCalls);
        Assert.Single(dialog.Errors);
        Assert.Equal("0..65535", viewModel.ParameterRows[0].ErrorText);
    }

    [Fact]
    public async Task WriteParametersShowsModalErrorWhenWriteFails()
    {
        var service = new FakeModbusTcpService
        {
            NextSetResult = ModbusOperationResult.Failure("WriteFailed", "Write failed.", "No ACK")
        };
        var dialog = new FakeDialogService();
        using var viewModel = CreateViewModel(service, dialog);

        foreach (var parameter in viewModel.ParameterRows)
        {
            parameter.EditValueText = "5";
        }

        await viewModel.WriteParametersCommand.Execute().FirstAsync().ToTask();

        Assert.Single(service.SetCalls);
        Assert.Single(dialog.Errors);
        Assert.Contains("Write failed", dialog.Errors[0].Message);
    }

    [Fact]
    public void ButtonStatesReflectDemoFacadeState()
    {
        var service = new FakeModbusTcpService();
        using var viewModel = CreateViewModel(service);

        Assert.True(viewModel.CanStartClient);
        Assert.True(viewModel.CanStartServer);
        Assert.False(viewModel.CanStop);
        Assert.True(viewModel.CanOpenSettings);

        service.PublishState(new ModbusServiceState(
            ModbusRunMode.Both,
            ModbusConnectionState.Running,
            ModbusConnectionState.Running,
            false,
            "Client and server running",
            null,
            DateTimeOffset.Now));

        Assert.False(viewModel.CanStartClient);
        Assert.False(viewModel.CanStartServer);
        Assert.True(viewModel.CanStop);
        Assert.False(viewModel.CanOpenSettings);
    }

    [Fact]
    public void StateChangedIsScheduledThroughUiDispatcher()
    {
        var service = new FakeModbusTcpService();
        var postedActions = new Queue<Action>();
        using var viewModel = CreateViewModel(service, uiDispatcher: postedActions.Enqueue);
        var state = new ModbusServiceState(
            ModbusRunMode.Client,
            ModbusConnectionState.Reconnecting,
            ModbusConnectionState.Stopped,
            true,
            "Сервер недоступен, клиент ожидает подключения",
            "Connection refused",
            DateTimeOffset.Now);

        service.PublishState(state);

        Assert.False(viewModel.IsWaitingForConnection);
        Assert.Single(postedActions);

        postedActions.Dequeue().Invoke();

        Assert.True(viewModel.IsWaitingForConnection);
        Assert.Contains("клиент ожидает подключения", viewModel.StatusText);
        Assert.Equal("Connection refused", viewModel.LastError);
    }

    [Fact]
    public async Task StartClientCommandReturnsControlBeforeFacadeStartCompletes()
    {
        var service = new FakeModbusTcpService
        {
            StartClientSynchronousDelay = TimeSpan.FromMilliseconds(350)
        };
        using var viewModel = CreateViewModel(service);

        var stopwatch = Stopwatch.StartNew();
        var commandTask = viewModel.StartClientCommand.Execute().FirstAsync().ToTask();
        var returnedAfter = stopwatch.Elapsed;

        await commandTask.WaitAsync(TimeSpan.FromMilliseconds(150));
        Assert.True(
            returnedAfter < TimeSpan.FromMilliseconds(150),
            $"StartClientCommand blocked the caller for {returnedAfter.TotalMilliseconds:0} ms.");
        Assert.True(viewModel.IsLifecycleOperationActive);
        Assert.True(viewModel.IsCommandRunning);

        Assert.True(await WaitForAsync(() => service.StartClientCount == 1));
        Assert.True(await WaitForAsync(() => !viewModel.IsLifecycleOperationActive));
        Assert.Equal(1, service.StartClientCount);
    }

    [Fact]
    public async Task StartClientStartingStateIsQueuedUntilUiDispatcherRuns()
    {
        var service = new FakeModbusTcpService
        {
            PublishStartingStateOnStartClient = true,
            StartClientSynchronousDelay = TimeSpan.FromMilliseconds(100)
        };
        var postedActions = new ConcurrentQueue<Action>();
        using var viewModel = CreateViewModel(service, uiDispatcher: postedActions.Enqueue);

        var commandTask = viewModel.StartClientCommand.Execute().FirstAsync().ToTask();
        await commandTask.WaitAsync(TimeSpan.FromMilliseconds(150));
        Assert.True(await WaitForAsync(() => postedActions.Count > 0));

        Assert.False(viewModel.IsWaitingForConnection);
        Assert.Equal("None: Modbus stopped.", viewModel.StatusText);

        Assert.True(postedActions.TryDequeue(out var applyStartingState));
        applyStartingState();

        Assert.True(viewModel.IsWaitingForConnection);
        Assert.Contains("Клиент подключается", viewModel.StatusText);

        Assert.True(await WaitForAsync(() => postedActions.Count > 0));
        while (postedActions.TryDequeue(out var queuedAction))
        {
            queuedAction();
        }

        Assert.False(viewModel.IsLifecycleOperationActive);
    }

    [Fact]
    public async Task StopCommandCancelsActiveLifecycleWithoutModalError()
    {
        var service = new FakeModbusTcpService
        {
            BlockStartClientUntilCanceled = true
        };
        var dialog = new FakeDialogService();
        using var viewModel = CreateViewModel(service, dialog);

        await viewModel.StartClientCommand.Execute().FirstAsync().ToTask();
        Assert.True(await WaitForAsync(() => service.StartClientCount == 1));
        Assert.True(viewModel.IsLifecycleOperationActive);
        Assert.True(viewModel.CanStop);

        await viewModel.StopCommand.Execute().FirstAsync().ToTask();

        Assert.True(await WaitForAsync(
            () =>
                service.StartClientObservedCancellation
                && service.StopCount == 1
                && !viewModel.IsLifecycleOperationActive));
        Assert.Empty(dialog.Errors);
    }

    [Fact]
    public async Task StartCommandsCallDemoFacade()
    {
        var service = new FakeModbusTcpService();
        using var viewModel = CreateViewModel(service);

        await viewModel.StartClientCommand.Execute().FirstAsync().ToTask();
        Assert.True(await WaitForAsync(() => service.StartClientCount == 1 && !viewModel.IsLifecycleOperationActive));
        await viewModel.StartServerCommand.Execute().FirstAsync().ToTask();

        Assert.True(await WaitForAsync(() => service.StartServerCount == 1 && !viewModel.IsLifecycleOperationActive));
        Assert.Equal(1, service.StartClientCount);
        Assert.Equal(1, service.StartServerCount);
    }

    [Fact]
    public async Task SettingsCommandUsesDemoSectionAndNextStartUsesSavedOptions()
    {
        var service = new FakeModbusTcpService();
        var dialog = new FakeDialogService();
        var savedOptions = CreateOptions();
        savedOptions.Client.Port = 2503;
        dialog.NextModbusResult = savedOptions;
        using var viewModel = CreateViewModel(service, dialog);

        await viewModel.OpenSettingsCommand.Execute().FirstAsync().ToTask();
        await viewModel.StartClientCommand.Execute().FirstAsync().ToTask();

        Assert.True(await WaitForAsync(() => service.LastStartClientOptions is not null));
        Assert.Equal(ModbusOptions.DemoSectionName, dialog.LastModbusSectionName);
        Assert.Equal(2503, service.LastStartClientOptions?.Client.Port);
    }

    private static ModbusCommandBitRow FindCommand(
        ModbusDemoViewModel viewModel,
        string pointName,
        int bitIndex)
        => viewModel.CommandGroups
            .SelectMany(group => group.Rows)
            .Single(row => row.PointName == pointName && row.BitIndex == bitIndex);

    private static ModbusOptions CreateOptions()
        => new()
        {
            Client = CreateEndpoint(),
            Server = CreateEndpoint(),
            DataMap =
            [
                CreatePoint("Telemetry_1", 0, ModbusDataAccess.Read),
                CreatePoint("Telemetry_2", 1, ModbusDataAccess.Read),
                CreatePoint("Telemetry_3", 2, ModbusDataAccess.Read),
                CreatePoint("Telemetry_4", 3, ModbusDataAccess.Read),
                CreatePoint("Commands_1", 4, ModbusDataAccess.Write),
                CreatePoint("Commands_2", 5, ModbusDataAccess.Write),
                CreatePoint("Commands_3", 6, ModbusDataAccess.Write),
                CreatePoint("Commands_4", 7, ModbusDataAccess.Write),
                CreatePoint("MB_ТЕКУЩАЯ_ПОЗИЦИЯ", 16, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_N_АВАРИЯ-ПОЗ_УПРАВ", 17, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_N_АВАРИЯ-ВРАЩЕНИЕ", 18, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_СТАТУС-ВРАЩЕНИЕ", 19, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_Hz", 20, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_ЦЕЛЬ_ПОЗИЦИЯ", 27, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_ВОЗВРАТ_ПОЗИЦИЯ", 28, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_ТОП_СБРОС-ПОЗ_УПРАВ", 29, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_ТОП_ФИЛЬТР-ПОЗ_УПРАВ", 30, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_ТОП_АВАРИЯ-ПОЗ_УПРАВ", 31, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_ТОП_ПАУЗА-ВРАЩЕНИЕ", 32, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_ТОП_АВАРИЯ-ВРАЩЕНИЕ", 33, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_ТОП_СБРОС-З_ВЫГРУЗКА", 34, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_ТОП_СБРОС-З_ЗАГРУЗКА", 35, ModbusDataAccess.ReadWrite)
            ]
        };

    private static ModbusEndpointOptions CreateEndpoint()
        => new()
        {
            CoilsEnabled = false,
            HoldingRegistersEnabled = true,
            CoilStartAddress = 0,
            HoldingRegisterStartAddress = 16384,
            CoilCount = 0,
            RegisterCount = 36
        };

    private static ModbusDataPointOptions CreatePoint(
        string name,
        int address,
        ModbusDataAccess access)
        => new()
        {
            Name = name,
            Area = ModbusDataArea.HoldingRegister,
            Address = address,
            Length = 1,
            Access = access,
            Type = ModbusValueType.UInt16
        };

    private static ModbusDemoViewModel CreateViewModel(
        FakeModbusTcpService service,
        FakeDialogService? dialog = null,
        Action<Action>? uiDispatcher = null)
    {
        var options = CreateOptions();
        return new ModbusDemoViewModel(
            service,
            new TestOptionsProvider(options),
            dialog ?? new FakeDialogService(),
            new FakeAppConfigService(options),
            uiDispatcher ?? (action => action()));
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        var startedAt = DateTimeOffset.UtcNow;

        while (DateTimeOffset.UtcNow - startedAt < TimeSpan.FromSeconds(2))
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return false;
    }

    private sealed class TestOptionsProvider(ModbusOptions currentValue) : IModbusDemoOptionsProvider
    {
        public ModbusOptions CurrentValue { get; } = currentValue;
    }

    private sealed class FakeAppConfigService(ModbusOptions options) : IAppConfigService
    {
        public T GetSection<T>(string sectionName) where T : class, new()
            => options.Clone() as T ?? new T();

        public string GetValue(string key)
            => string.Empty;

        public Task SaveSectionAsync<T>(string sectionName, T value, CancellationToken ct = default)
            => Task.CompletedTask;

        public void SaveUserSettings(UserSettings settings)
        {
        }

        public UserSettings LoadUserSettings()
            => new();
    }

    private sealed class FakeModbusTcpService : IModbusDemoTcpService
    {
        private readonly Dictionary<string, List<Action<ModbusDataValue>>> _subscriptions = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Name, ushort Value)> SetCalls { get; } = [];

        public int StartClientCount { get; private set; }

        public int StartServerCount { get; private set; }

        public int StopCount { get; private set; }

        public ModbusOptions? LastStartClientOptions { get; private set; }

        public ModbusOptions? LastStartServerOptions { get; private set; }

        public TimeSpan StartClientSynchronousDelay { get; init; }

        public bool PublishStartingStateOnStartClient { get; init; }

        public bool BlockStartClientUntilCanceled { get; init; }

        public bool StartClientObservedCancellation { get; private set; }

        public ModbusOperationResult NextSetResult { get; set; } = ModbusOperationResult.Success();

        public Queue<ModbusOperationResult> SetResults { get; } = [];

        public TaskCompletionSource<int>? SetCallStarted { get; set; }

        public TaskCompletionSource<bool>? SetCallRelease { get; set; }

        public ModbusServiceState State { get; private set; } = ModbusServiceState.Stopped;

        public event EventHandler<ModbusServiceState>? StateChanged;

        public async Task<ModbusOperationResult> StartClientAsync(ModbusOptions? options = null, CancellationToken ct = default)
        {
            StartClientCount++;
            LastStartClientOptions = options?.Clone();

            if (PublishStartingStateOnStartClient)
            {
                PublishState(new ModbusServiceState(
                    ModbusRunMode.Client,
                    ModbusConnectionState.Starting,
                    ModbusConnectionState.Stopped,
                    true,
                    "Клиент подключается",
                    null,
                    DateTimeOffset.Now));
            }

            if (BlockStartClientUntilCanceled)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    StartClientObservedCancellation = true;
                    throw;
                }
            }

            if (StartClientSynchronousDelay > TimeSpan.Zero)
            {
                Thread.Sleep(StartClientSynchronousDelay);
            }

            return ModbusOperationResult.Success();
        }

        public Task<ModbusOperationResult> StartServerAsync(ModbusOptions? options = null, CancellationToken ct = default)
        {
            StartServerCount++;
            LastStartServerOptions = options?.Clone();
            return Task.FromResult(ModbusOperationResult.Success());
        }

        public Task<ModbusOperationResult> StopAsync(CancellationToken ct = default)
        {
            StopCount++;
            return Task.FromResult(ModbusOperationResult.Success());
        }

        public Task<ModbusOperationResult<T>> GetAsync<T>(string name, CancellationToken ct = default)
            => Task.FromResult(ModbusOperationResult<T>.Failure("NotImplemented", "Not implemented."));

        public async Task<ModbusOperationResult> SetAsync<T>(string name, T value, CancellationToken ct = default)
        {
            SetCalls.Add((name, Convert.ToUInt16(value)));
            SetCallStarted?.TrySetResult(SetCalls.Count);

            if (SetCallRelease is not null)
            {
                await SetCallRelease.Task.WaitAsync(ct);
            }

            return SetResults.Count > 0
                ? SetResults.Dequeue()
                : NextSetResult;
        }

        public IDisposable Subscribe(string name, Action<ModbusDataValue> onChanged)
        {
            if (!_subscriptions.TryGetValue(name, out var subscribers))
            {
                subscribers = [];
                _subscriptions[name] = subscribers;
            }

            subscribers.Add(onChanged);
            return new Subscription(() => subscribers.Remove(onChanged));
        }

        public void Publish(string name, object? value)
        {
            if (!_subscriptions.TryGetValue(name, out var subscribers))
            {
                return;
            }

            var dataValue = new ModbusDataValue(
                name,
                ModbusDataArea.HoldingRegister,
                0,
                1,
                ModbusValueType.UInt16,
                value,
                DateTimeOffset.Now);
            foreach (var subscriber in subscribers.ToArray())
            {
                subscriber(dataValue);
            }
        }

        public void PublishState(ModbusServiceState state)
        {
            State = state;
            StateChanged?.Invoke(this, state);
        }

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;

        private sealed class Subscription(Action dispose) : IDisposable
        {
            public void Dispose()
                => dispose();
        }
    }

    private sealed class FakeDialogService : IDialogService
    {
        public List<(string Title, string Message, string? Details)> Errors { get; } = [];

        public string? LastModbusSectionName { get; private set; }

        public ModbusOptions? NextModbusResult { get; set; }

        public Task<bool> ConfirmAsync(string message, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<string?> RequestSecretAsync(string message, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task ShowErrorAsync(string title, string message, string? details = null, CancellationToken ct = default)
        {
            Errors.Add((title, message, details));
            return Task.CompletedTask;
        }

        public Task<ModbusOptions?> EditModbusSettingsAsync(
            string title,
            string sectionName,
            ModbusOptions options,
            CancellationToken ct = default)
        {
            LastModbusSectionName = sectionName;
            return Task.FromResult(NextModbusResult?.Clone());
        }

        public Task<OpcUaConfiguredTag?> EditOpcUaTagAsync(
            string title,
            OpcUaConfiguredTag? tag,
            OpcUaImportTarget target,
            CancellationToken ct = default)
            => Task.FromResult<OpcUaConfiguredTag?>(null);

        public Task<OpcUaTagImportResult?> ImportOpcUaTagsAsync(
            OpcUaBrowseRequest request,
            CancellationToken ct = default)
            => Task.FromResult<OpcUaTagImportResult?>(null);
    }
}
