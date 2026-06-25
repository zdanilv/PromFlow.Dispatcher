using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Licensing;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Runtime;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Configurator.Tests.Unit.Runtime;

public sealed class ApplicationRuntimeCoordinatorTests
{
    [Fact]
    public async Task StartAsync_RunsCentralStartupAndAutostartsModbusBeforeRouting()
    {
        var fixture = new RuntimeFixture
        {
            ModbusOptions =
            {
                AutostartOnWorkspaceOpen = true,
                StartupMode = ModbusRunMode.Client
            }
        };
        var coordinator = fixture.CreateCoordinator();

        await coordinator.StartAsync();

        Assert.True(coordinator.Current.IsRunning);
        Assert.Equal(
            [
                "instance:acquire",
                "persistence:init",
                "identity:get-or-create",
                "license:refresh",
                "archive:start",
                "collector:start",
                "modbus:start:Client"
            ],
            fixture.Order);
        Assert.Contains(coordinator.Current.StartupSteps, step => step.Step == "ModbusAutostart");
    }

    [Fact]
    public async Task StartAsync_FatalPersistenceFailureTransitionsFaultedAndReleasesInstance()
    {
        var fixture = new RuntimeFixture();
        fixture.PersistenceSteps =
        [
            ApplicationRuntimeStepResult.Failure(
                "SecurityPersistenceMigrations",
                "SecurityMigrationFailed",
                "Security database is unavailable.",
                isFatal: true)
        ];
        var coordinator = fixture.CreateCoordinator();

        await Assert.ThrowsAsync<ApplicationRuntimeException>(() => coordinator.StartAsync());

        Assert.Equal(ApplicationRuntimePhase.Faulted, coordinator.Current.Phase);
        Assert.Equal(1, fixture.InstanceLease.DisposeCount);
        Assert.Equal(["instance:acquire", "persistence:init"], fixture.Order);
    }

    [Fact]
    public async Task StartAsync_NonFatalArchiveFailureSkipsCollectorAndStillRuns()
    {
        var fixture = new RuntimeFixture();
        fixture.ArchiveRuntime.StartResult = ArchiveOperationResult.Failure(
            "ArchiveUnavailable",
            "Archive cannot be started.");
        var coordinator = fixture.CreateCoordinator();

        await coordinator.StartAsync();

        Assert.True(coordinator.Current.IsRunning);
        Assert.Equal(0, fixture.ArchiveCollector.StartCount);
        Assert.Contains(coordinator.Current.StartupSteps, step =>
            step.Step == "ArchiveRuntimeStart"
            && !step.Succeeded
            && !step.IsFatal);
    }

    [Fact]
    public async Task StopAsync_MarksShuttingDownBeforeStoppingRuntimeAndFlushesArchive()
    {
        var fixture = new RuntimeFixture();
        var coordinator = fixture.CreateCoordinator();
        fixture.ModbusDemo.OnStop = () => Assert.True(coordinator.Current.IsShuttingDown);
        await coordinator.StartAsync();

        await coordinator.StopAsync();

        Assert.Equal(ApplicationRuntimePhase.Stopped, coordinator.Current.Phase);
        Assert.Equal(1, fixture.ModbusDemo.StopCount);
        Assert.Equal(1, fixture.ArchiveCollector.StopCount);
        Assert.Equal(1, fixture.ArchiveRuntime.FlushCount);
        Assert.Equal(1, fixture.ArchiveRuntime.StopCount);
        Assert.Equal(1, fixture.InstanceLease.DisposeCount);
    }

    [Fact]
    public void RuntimeCommandDeliveryGate_DeniesOnlyNonEmergencyCommandsDuringShutdown()
    {
        var state = new MutableRuntimeStateAccessor();
        var gate = new RuntimeCommandDeliveryGate(state);
        state.SetPhase(ApplicationRuntimePhase.ShuttingDown);

        var ordinary = gate.Evaluate(CreateContext("ordinary.command", isEmergency: false));
        var emergency = gate.Evaluate(CreateContext("system.emergency", isEmergency: true));

        Assert.False(ordinary.Allowed);
        Assert.Equal(RuntimeCommandDeliveryGate.ShuttingDownErrorCode, ordinary.ErrorCode);
        Assert.True(emergency.Allowed);
    }

    private static CommandExecutionContext CreateContext(string signalId, bool isEmergency)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            sessionId: null,
            userId: null,
            username: null,
            "device-1",
            signalId,
            SignalValueType.Bool,
            "true",
            ModbusWriteMode.Latched,
            isEmergency,
            schemaVersion: 1);

    private sealed class RuntimeFixture
    {
        public List<string> Order { get; } = [];

        public RecordingApplicationInstanceLease InstanceLease { get; } = new();

        public IReadOnlyList<ApplicationRuntimeStepResult> PersistenceSteps { get; set; } =
        [
            ApplicationRuntimeStepResult.Success("SecurityPersistenceMigrations"),
            ApplicationRuntimeStepResult.Success("ArchivePersistenceSkipped")
        ];

        public ModbusOptions ModbusOptions { get; } = new()
        {
            AutostartOnWorkspaceOpen = false,
            StartupMode = ModbusRunMode.None
        };

        public RecordingArchiveRuntime ArchiveRuntime { get; }

        public RecordingArchiveCollector ArchiveCollector { get; }

        public RecordingModbusRuntime ModbusRuntime { get; }

        public RecordingModbusDemoTcpService ModbusDemo { get; }

        public RuntimeFixture()
        {
            ArchiveRuntime = new RecordingArchiveRuntime(Order);
            ArchiveCollector = new RecordingArchiveCollector(Order);
            ModbusRuntime = new RecordingModbusRuntime(Order);
            ModbusDemo = new RecordingModbusDemoTcpService(Order);
        }

        public ApplicationRuntimeCoordinator CreateCoordinator()
            => new(
                new RecordingInstanceGuard(Order, InstanceLease),
                new RecordingPersistenceInitializer(Order, () => PersistenceSteps),
                new RecordingInstallationIdentityService(Order),
                new RecordingLicenseService(Order),
                ArchiveRuntime,
                ArchiveCollector,
                ModbusRuntime,
                ModbusDemo,
                new StaticModbusOptionsProvider(ModbusOptions),
                new ApplicationLifecycleOptions(),
                NullLogger<ApplicationRuntimeCoordinator>.Instance);
    }

    private sealed class RecordingInstanceGuard(
        List<string> order,
        RecordingApplicationInstanceLease lease) : IApplicationInstanceGuard
    {
        public Task<IApplicationInstanceLease> AcquireAsync(CancellationToken cancellationToken = default)
        {
            order.Add("instance:acquire");
            return Task.FromResult<IApplicationInstanceLease>(lease);
        }
    }

    private sealed class RecordingApplicationInstanceLease : IApplicationInstanceLease
    {
        public int DisposeCount { get; private set; }

        public string Description => "test";

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingPersistenceInitializer(
        List<string> order,
        Func<IReadOnlyList<ApplicationRuntimeStepResult>> getSteps) : IPersistenceInitializer
    {
        public Task<PersistenceInitializationResult> InitializeAsync(CancellationToken cancellationToken = default)
        {
            order.Add("persistence:init");
            return Task.FromResult(new PersistenceInitializationResult(getSteps()));
        }
    }

    private sealed class RecordingInstallationIdentityService(List<string> order) : IInstallationIdentityService
    {
        public Task<InstallationIdentity> GetOrCreateAsync(CancellationToken cancellationToken = default)
        {
            order.Add("identity:get-or-create");
            return Task.FromResult(new InstallationIdentity("installation-1", DateTimeOffset.UtcNow));
        }

        public Task<InstallationIdentityRequest> CreateRequestAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingLicenseService(List<string> order) : ILicenseService
    {
        public Task<LicenseState> GetCurrentAsync(CancellationToken cancellationToken = default)
            => RefreshAsync(cancellationToken);

        public Task<LicenseState> RefreshAsync(CancellationToken cancellationToken = default)
        {
            order.Add("license:refresh");
            return Task.FromResult(LicenseState.Missing(DateTimeOffset.UtcNow));
        }

        public Task<LicenseInstallResult> InstallAsync(
            LicenseInstallRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<LicenseValidationResult> VerifyAsync(
            byte[] licenseBytes,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingArchiveRuntime(List<string> order) : IArchiveRuntime
    {
        public ArchiveOperationResult StartResult { get; set; } = ArchiveOperationResult.Success();

        public int FlushCount { get; private set; }

        public int StopCount { get; private set; }

        public Task<ArchiveOperationResult> StartAsync(CancellationToken cancellationToken = default)
        {
            order.Add("archive:start");
            return Task.FromResult(StartResult);
        }

        public Task<ArchiveOperationResult> FlushAsync(CancellationToken cancellationToken = default)
        {
            FlushCount++;
            order.Add("archive:flush");
            return Task.FromResult(ArchiveOperationResult.Success());
        }

        public Task<ArchiveOperationResult> StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            order.Add("archive:stop");
            return Task.FromResult(ArchiveOperationResult.Success());
        }

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

    private sealed class RecordingArchiveCollector(List<string> order) : IModbusArchiveCollector
    {
        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public Task<ArchiveOperationResult> StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            order.Add("collector:start");
            return Task.FromResult(ArchiveOperationResult.Success());
        }

        public Task<ArchiveOperationResult> StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            order.Add("collector:stop");
            return Task.FromResult(ArchiveOperationResult.Success());
        }

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

    private sealed class StaticModbusOptionsProvider(ModbusOptions options) : IModbusDemoOptionsProvider
    {
        public ModbusOptions CurrentValue { get; } = options;
    }

    private sealed class RecordingModbusRuntime(List<string> order) : IModbusRuntimeService
    {
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
            order.Add("modbus:start:" + mode);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RestartAsync(
            ModbusRunMode mode = ModbusRunMode.Both,
            ModbusOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopClientAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RestartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopServerAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RestartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

    private sealed class RecordingModbusDemoTcpService(List<string> order) : IModbusDemoTcpService
    {
        public int StopCount { get; private set; }

        public Action? OnStop { get; set; }

        public ModbusServiceState State => ModbusServiceState.Stopped;

        public event EventHandler<ModbusServiceState>? StateChanged { add { } remove { } }

        public Task<ModbusOperationResult> StartClientAsync(ModbusOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(ModbusOperationResult.Success());

        public Task<ModbusOperationResult> StartServerAsync(ModbusOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(ModbusOperationResult.Success());

        public Task<ModbusOperationResult> StopAsync(CancellationToken ct = default)
        {
            StopCount++;
            OnStop?.Invoke();
            order.Add("modbus:stop");
            return Task.FromResult(ModbusOperationResult.Success());
        }

        public Task<ModbusOperationResult<T>> GetAsync<T>(string name, CancellationToken ct = default)
            => Task.FromResult(ModbusOperationResult<T>.Failure("Unavailable", "Unavailable"));

        public Task<ModbusOperationResult> SetAsync<T>(string name, T value, CancellationToken ct = default)
            => SetAsync(name, value, context: null, ct);

        public Task<ModbusOperationResult> SetAsync<T>(
            string name,
            T value,
            CommandExecutionContext? context,
            CancellationToken ct = default)
            => Task.FromResult(ModbusOperationResult.Success());

        public IDisposable Subscribe(string name, Action<ModbusDataValue> onChanged)
            => new NoopDisposable();

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

    private sealed class MutableRuntimeStateAccessor : IApplicationRuntimeStateAccessor
    {
        public ApplicationRuntimeSnapshot Current { get; private set; } = ApplicationRuntimeSnapshot.Initial;

        public event EventHandler<ApplicationRuntimeStateChangedEventArgs>? StateChanged;

        public void SetPhase(ApplicationRuntimePhase phase)
        {
            var previous = Current;
            Current = new ApplicationRuntimeSnapshot(
                phase,
                Array.Empty<ApplicationRuntimeStepResult>(),
                Array.Empty<ApplicationRuntimeStepResult>());
            StateChanged?.Invoke(this, new ApplicationRuntimeStateChangedEventArgs(previous, Current));
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
