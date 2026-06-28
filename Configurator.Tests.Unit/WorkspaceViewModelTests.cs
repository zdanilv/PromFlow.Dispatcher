using System.Runtime.CompilerServices;
using Configurator.Application.Services;
using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Dialogs;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Desktop.Workspace;
using Configurator.Desktop.Workspace.Alarms;
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
        var alarmManager = Uninitialized<AlarmManagerViewModel>();
        var routeMapDashboard = Uninitialized<RouteMapDashboardViewModel>();
        var routeMapSignalMapping = Uninitialized<RouteMapSignalMappingViewModel>();

        var viewModel = CreateWorkspaceViewModel(
            applicationOptions: new ApplicationOptions { WorkMode = ApplicationOptions.UserWorkMode },
            modbusDemo: modbusDemo,
            alarmManager: alarmManager,
            routeMapDashboard: routeMapDashboard,
            routeMapSignalMapping: routeMapSignalMapping);

        Assert.True(viewModel.IsUserMode);
        Assert.False(viewModel.IsAdminMode);
        Assert.Same(modbusDemo, viewModel.ModbusDemo);
        Assert.Same(alarmManager, viewModel.AlarmManager);
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
        Assert.Equal(1, runtime.SnapshotSubscriptions);
    }

    [Fact]
    public void AdminModeStartsAlarmMonitor()
    {
        var runtime = new RecordingModbusRuntime();

        _ = CreateWorkspaceViewModel(
            applicationOptions: new ApplicationOptions { WorkMode = ApplicationOptions.AdminWorkMode },
            modbusRuntime: runtime);

        Assert.Equal(1, runtime.SnapshotSubscriptions);
    }

    [Fact]
    public void AlarmManager_AddAlarm_DoesNotRecurseWhenPhysicalAddressFieldsRefresh()
    {
        using var viewModel = new AlarmManagerViewModel(
            new StaticOptionsMonitor(new ModbusOptions
            {
                Client = new ModbusEndpointOptions
                {
                    CoilCount = 10,
                    RegisterCount = 10
                },
                Server = new ModbusEndpointOptions
                {
                    CoilCount = 10,
                    RegisterCount = 10
                }
            }),
            new NoOpAppConfigService(),
            new ModbusAlarmMapValidator());

        ((System.Windows.Input.ICommand)viewModel.AddAlarmCommand).Execute(null);

        Assert.Single(viewModel.Rows);
        Assert.True(viewModel.IsDirty);
        Assert.False(viewModel.Rows[0].HasError);
    }

    [Fact]
    public void AlarmManagerRow_SettingCurrentPhysicalAddress_DoesNotReemitPhysicalAddressChange()
    {
        var row = new AlarmManagerRow(new ModbusAlarmOptions
        {
            Id = "alarm.main",
            Message = "Alarm",
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
        });
        row.UpdateAddressBases(
            new ModbusEndpointOptions
            {
                CoilStartAddress = 100,
                CoilCount = 10,
                HoldingRegisterStartAddress = 200,
                RegisterCount = 10
            },
            new ModbusEndpointOptions
            {
                CoilStartAddress = 300,
                CoilCount = 10,
                HoldingRegisterStartAddress = 400,
                RegisterCount = 10
            });
        var current = row.AlarmClientPhysicalAddressText;
        var physicalAddressChanges = 0;
        row.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AlarmManagerRow.AlarmClientPhysicalAddressText))
            {
                physicalAddressChanges++;
            }
        };

        row.AlarmClientPhysicalAddressText = current;

        Assert.Equal(0, physicalAddressChanges);
        Assert.Equal(0, row.AlarmAddress);
        Assert.Null(row.PhysicalAddressError);
    }

    private static WorkspaceViewModel CreateWorkspaceViewModel(
        ApplicationOptions applicationOptions,
        ModbusDemoViewModel? modbusDemo = null,
        AlarmManagerViewModel? alarmManager = null,
        RouteMapDashboardViewModel? routeMapDashboard = null,
        RouteMapSignalMappingViewModel? routeMapSignalMapping = null,
        RecordingModbusRuntime? modbusRuntime = null,
        StaticModbusDemoOptionsProvider? modbusOptions = null)
    {
        var runtime = modbusRuntime ?? new RecordingModbusRuntime();
        return new WorkspaceViewModel(
            hostScreen: new TestScreen(),
            authService: new TestAuthApp(),
            modbusDemo: modbusDemo!,
            alarmManager: alarmManager ?? Uninitialized<AlarmManagerViewModel>(),
            alarmMonitor: CreateAlarmMonitor(runtime),
            routeMapDashboard: routeMapDashboard!,
            routeMapSignalMapping: routeMapSignalMapping!,
            modbusRuntime: runtime,
            modbusOptions: modbusOptions ?? new StaticModbusDemoOptionsProvider(new ModbusOptions()),
            applicationOptions: Options.Create(applicationOptions),
            logger: NullLogger<WorkspaceViewModel>.Instance);
    }

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

    private static ModbusAlarmMonitor CreateAlarmMonitor(IModbusRuntimeService runtime)
        => new(
            new StaticOptionsMonitor(new ModbusOptions()),
            runtime,
            new NoOpDialogService(),
            new NoOpBitWriter(),
            NullLogger<ModbusAlarmMonitor>.Instance);

    private sealed class StaticOptionsMonitor(ModbusOptions options) : IOptionsMonitor<ModbusOptions>
    {
        public ModbusOptions CurrentValue { get; } = options;
        public ModbusOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<ModbusOptions, string?> listener) => null;
    }

    private sealed class NoOpBitWriter : IModbusBitWriter
    {
        public Task<ModbusOperationResult> PulseAsync(
            ModbusBitAddressOptions address,
            int pulseDurationMs,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ModbusOperationResult.Success());
    }

    private sealed class NoOpDialogService : IDialogService
    {
        public Task<bool> ConfirmAsync(string message, CancellationToken ct = default) => Task.FromResult(false);
        public Task<string?> RequestSecretAsync(string message, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task ShowErrorAsync(string title, string message, string? details = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ShowAlarmNotificationAsync(ModbusAlarmKind kind, string message, CancellationToken ct = default) => Task.FromResult(false);
        public Task<ModbusOptions?> EditModbusSettingsAsync(string title, string sectionName, ModbusOptions options, CancellationToken ct = default) => Task.FromResult<ModbusOptions?>(null);
        public Task<OpcUaConfiguredTag?> EditOpcUaTagAsync(string title, OpcUaConfiguredTag? tag, OpcUaImportTarget target, CancellationToken ct = default) => Task.FromResult<OpcUaConfiguredTag?>(null);
        public Task<OpcUaTagImportResult?> ImportOpcUaTagsAsync(OpcUaBrowseRequest request, CancellationToken ct = default) => Task.FromResult<OpcUaTagImportResult?>(null);
    }

    private sealed class NoOpAppConfigService : IAppConfigService
    {
        public T GetSection<T>(string sectionName) where T : class, new() => new();
        public string GetValue(string key) => string.Empty;
        public Task SaveSectionAsync<T>(string sectionName, T value, CancellationToken ct = default) => Task.CompletedTask;
        public void SaveUserSettings(UserSettings settings) { }
        public UserSettings LoadUserSettings() => new();
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
        public int SnapshotSubscriptions { get; private set; }
        public event EventHandler<ModbusStatus>? StatusChanged { add { } remove { } }
        public event EventHandler<ModbusSnapshot>? SnapshotChanged
        {
            add => SnapshotSubscriptions++;
            remove => SnapshotSubscriptions--;
        }

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
