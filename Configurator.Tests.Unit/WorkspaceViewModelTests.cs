using System.Runtime.CompilerServices;
using Configurator.Application.Services;
using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Desktop.Workspace;
using Configurator.Desktop.Workspace.ModbusDemo;
using Configurator.Desktop.Workspace.RouteMap.SignalMapping;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ReactiveUI;
using Xunit;

namespace Configurator.Tests.Unit;

public sealed class WorkspaceViewModelTests
{
    [Fact]
    public void UserModeKeepsHiddenWorkspaceViewModels()
    {
        var modbusDemo = Uninitialized<ModbusDemoViewModel>();
        var routeMapDashboard = Uninitialized<RouteMapDashboardViewModel>();
        var routeMapSignalMapping = Uninitialized<RouteMapSignalMappingViewModel>();

        var viewModel = CreateWorkspaceViewModel(
            applicationOptions: new ApplicationOptions { WorkMode = ApplicationOptions.UserWorkMode },
            modbusDemo: modbusDemo,
            routeMapDashboard: routeMapDashboard,
            routeMapSignalMapping: routeMapSignalMapping);

        Assert.True(viewModel.IsUserMode);
        Assert.False(viewModel.IsAdminMode);
        Assert.Same(modbusDemo, viewModel.ModbusDemo);
        Assert.Same(routeMapDashboard, viewModel.RouteMapDashboard);
        Assert.Same(routeMapSignalMapping, viewModel.RouteMapSignalMapping);
    }

    [Fact]
    public async Task UserModeAutostartsModbusRuntimeWhenConfigured()
    {
        var runtime = new RecordingModbusRuntime();
        var options = new ModbusOptions
        {
            AutostartOnWorkspaceOpen = true,
            StartupMode = ModbusRunMode.Client
        };

        _ = CreateWorkspaceViewModel(
            applicationOptions: new ApplicationOptions { WorkMode = ApplicationOptions.UserWorkMode },
            modbusRuntime: runtime,
            modbusOptions: new StaticModbusDemoOptionsProvider(options));

        var call = await runtime.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ModbusRunMode.Client, call.Mode);
        Assert.NotNull(call.Options);
        Assert.Equal(ModbusRunMode.Client, call.Options.StartupMode);
    }

    private static WorkspaceViewModel CreateWorkspaceViewModel(
        ApplicationOptions applicationOptions,
        ModbusDemoViewModel? modbusDemo = null,
        RouteMapDashboardViewModel? routeMapDashboard = null,
        RouteMapSignalMappingViewModel? routeMapSignalMapping = null,
        RecordingModbusRuntime? modbusRuntime = null,
        StaticModbusDemoOptionsProvider? modbusOptions = null) =>
        new(
            hostScreen: new TestScreen(),
            authService: new TestAuthApp(),
            modbusDemo: modbusDemo!,
            routeMapDashboard: routeMapDashboard!,
            routeMapSignalMapping: routeMapSignalMapping!,
            modbusRuntime: modbusRuntime ?? new RecordingModbusRuntime(),
            modbusOptions: modbusOptions ?? new StaticModbusDemoOptionsProvider(new ModbusOptions()),
            applicationOptions: Options.Create(applicationOptions),
            logger: NullLogger<WorkspaceViewModel>.Instance);

    private static T Uninitialized<T>() where T : class =>
        (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private sealed class TestScreen : IScreen
    {
        public RoutingState Router { get; } = new();
    }

    private sealed class TestAuthApp : IAuthApp
    {
        public bool IsAuthenticated { get; set; } = true;
        public bool Authenticate(string username, string password) => IsAuthenticated;
    }

    private sealed class StaticModbusDemoOptionsProvider(ModbusOptions options) : IModbusDemoOptionsProvider
    {
        public ModbusOptions CurrentValue { get; } = options;
    }

    private sealed record StartCall(ModbusRunMode Mode, ModbusOptions? Options);

    private sealed class RecordingModbusRuntime : IModbusRuntimeService
    {
        public TaskCompletionSource<StartCall> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ModbusStatus Status => ModbusStatus.Stopped;
        public ModbusSnapshot ClientSnapshot => ModbusSnapshot.Empty;
        public ModbusSnapshot ServerSnapshot => ModbusSnapshot.Empty;
        public ModbusOptions CurrentOptions { get; } = new();
        public event EventHandler<ModbusStatus>? StatusChanged { add { } remove { } }
        public event EventHandler<ModbusSnapshot>? SnapshotChanged { add { } remove { } }

        public Task StartAsync(
            ModbusRunMode mode = ModbusRunMode.Both,
            ModbusOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(new StartCall(mode, options?.Clone()));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RestartAsync(
            ModbusRunMode mode = ModbusRunMode.Both,
            ModbusOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StartClientAsync(
            ModbusOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StopClientAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RestartClientAsync(
            ModbusOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StartServerAsync(
            ModbusOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StopServerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RestartServerAsync(
            ModbusOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
