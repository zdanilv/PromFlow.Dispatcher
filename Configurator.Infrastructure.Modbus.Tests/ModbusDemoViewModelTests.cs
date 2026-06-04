using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Desktop.Workspace.ModbusDemo;
using Avalonia.Controls;
using ReactiveUI.Builder;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Xunit;
using static Configurator.Infrastructure.Modbus.Tests.ModbusDemoTestContext;
using FakeDialogService = Configurator.Infrastructure.Modbus.Tests.ModbusDemoTestContext.FakeDialogService;
using FakeModbusTcpService = Configurator.Infrastructure.Modbus.Tests.ModbusDemoTestContext.FakeModbusTcpService;

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
    public async Task Commands1ToggleButtonWritesHeldState()
    {
        var service = new FakeModbusTcpService();
        var dialog = new FakeDialogService();
        using var viewModel = CreateViewModel(service, dialog);
        var command = FindCommand(viewModel, "Commands_1", 2);

        command.IsChecked = true;
        Assert.True(await WaitForAsync(() => service.SetCalls.Count == 1));
        command.IsChecked = false;
        Assert.True(await WaitForAsync(() => service.SetCalls.Count == 2));

        Assert.Equal(ModbusCommandControlKind.ToggleButton, command.ControlKind);
        Assert.Equal(("Commands_1", (ushort)4), service.SetCalls[0]);
        Assert.Equal(("Commands_1", (ushort)0), service.SetCalls[1]);
        Assert.Empty(dialog.Errors);
    }

    [Fact]
    public async Task RadioButtonToggleCommandWritesHeldState()
    {
        var service = new FakeModbusTcpService();
        using var viewModel = CreateViewModel(service);
        var command = FindCommand(viewModel, "Commands_1", 0);

        command.IsChecked = true;
        Assert.True(await WaitForAsync(() => service.SetCalls.Count == 1));
        command.IsChecked = false;
        Assert.True(await WaitForAsync(() => service.SetCalls.Count == 2));

        Assert.Equal(ModbusCommandControlKind.RadioButtonToggle, command.ControlKind);
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
    public async Task Commands4ToggleButtonWritesHeldState()
    {
        var service = new FakeModbusTcpService();
        using var viewModel = CreateViewModel(service);
        var command = FindCommand(viewModel, "Commands_4", 0);

        command.IsChecked = true;
        Assert.True(await WaitForAsync(() => service.SetCalls.Count == 1));
        command.IsChecked = false;
        Assert.True(await WaitForAsync(() => service.SetCalls.Count == 2));

        Assert.Equal(ModbusCommandControlKind.ToggleButton, command.ControlKind);
        Assert.Equal(("Commands_4", (ushort)1), service.SetCalls[0]);
        Assert.Equal(("Commands_4", (ushort)0), service.SetCalls[1]);
    }

    [Fact]
    public async Task RadioButtonToggleBehaviorClearsAlreadyCheckedRadio()
    {
        var service = new FakeModbusTcpService();
        using var viewModel = CreateViewModel(service);
        var command = FindCommand(viewModel, "Commands_1", 0);
        var behavior = new ModbusDemoRadioButtonToggleBehavior();
        var radioButton = new RadioButton
        {
            DataContext = command,
            IsChecked = true
        };

        command.IsChecked = true;
        Assert.True(await WaitForAsync(() => service.SetCalls.Count == 1));
        service.SetCalls.Clear();

        Assert.True(behavior.ToggleOffIfChecked(radioButton));

        Assert.False(command.IsChecked);
        Assert.False(radioButton.IsChecked);
        Assert.True(await WaitForAsync(() => service.SetCalls.Count == 1));
        Assert.Equal(("Commands_1", (ushort)0), service.SetCalls[0]);
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
}
