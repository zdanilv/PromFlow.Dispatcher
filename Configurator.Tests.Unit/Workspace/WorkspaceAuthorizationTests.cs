using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Desktop.Workspace;
using Microsoft.Extensions.Logging.Abstractions;
using ReactiveUI;
using System.Reactive.Threading.Tasks;
using Xunit;

namespace Configurator.Tests.Unit.Workspace;

public sealed class WorkspaceAuthorizationTests
{
    [Fact]
    public async Task UserSessionCreatesOnlyRouteMapTabAndSkipsUnauthorizedFactories()
    {
        var fixture = new WorkspaceFixture(
            Permission.ViewRouteMap,
            Permission.IssueEquipmentCommands);
        using var workspace = fixture.CreateWorkspace();

        await workspace.InitializeAsync();

        Assert.Equal(["Route Map"], workspace.Tabs.Select(tab => tab.Header).ToArray());
        Assert.Equal(1, fixture.RouteMapFactoryCalls);
        Assert.Equal(0, fixture.SignalMappingFactoryCalls);
        Assert.Equal(0, fixture.ModbusDemoFactoryCalls);
    }

    [Fact]
    public async Task AdministratorSessionCreatesAllExistingStage9Tabs()
    {
        var fixture = new WorkspaceFixture(Enum.GetValues<Permission>());
        using var workspace = fixture.CreateWorkspace();

        await workspace.InitializeAsync();

        Assert.Equal(
            ["Route Map", "SignalId ↔ Modbus", "Modbus Demo"],
            workspace.Tabs.Select(tab => tab.Header).ToArray());
        Assert.Equal(1, fixture.RouteMapFactoryCalls);
        Assert.Equal(1, fixture.SignalMappingFactoryCalls);
        Assert.Equal(1, fixture.ModbusDemoFactoryCalls);
    }

    [Fact]
    public async Task DisposeDisposesCreatedTabContent()
    {
        var fixture = new WorkspaceFixture(Enum.GetValues<Permission>());
        var workspace = fixture.CreateWorkspace();
        await workspace.InitializeAsync();

        workspace.Dispose();

        Assert.All(fixture.CreatedContent, content => Assert.True(content.IsDisposed));
        Assert.Empty(workspace.Tabs);
    }

    [Fact]
    public async Task LogoutSignsOutAndInvokesOwnerCallback()
    {
        var fixture = new WorkspaceFixture(Permission.ViewRouteMap);
        using var workspace = fixture.CreateWorkspace();
        await workspace.InitializeAsync();

        await workspace.LogoutCommand.Execute().ToTask();

        Assert.Equal(1, fixture.AuthenticationService.SignOutCount);
        Assert.Equal(1, fixture.LogoutCallbackCount);
        Assert.False(fixture.SessionAccessor.Current.IsAuthenticated);
    }

    private sealed class WorkspaceFixture
    {
        private readonly IReadOnlyList<Permission> _permissions;

        public WorkspaceFixture(params Permission[] permissions)
        {
            _permissions = permissions;
            SessionAccessor.SetCurrent(new UserSession(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "operator",
                UserRole.User,
                _permissions,
                DateTimeOffset.UtcNow));
        }

        public TestSessionAccessor SessionAccessor { get; } = new();
        public FakeAuthenticationService AuthenticationService { get; } = new();
        public int RouteMapFactoryCalls { get; private set; }
        public int SignalMappingFactoryCalls { get; private set; }
        public int ModbusDemoFactoryCalls { get; private set; }
        public int LogoutCallbackCount { get; private set; }
        public List<DisposableContent> CreatedContent { get; } = [];

        public WorkspaceViewModel CreateWorkspace()
        {
            AuthenticationService.SessionAccessor = SessionAccessor;
            var access = new DefaultAccessDecisionService(SessionAccessor, new NoLicenseFeatureGate());
            return new WorkspaceViewModel(
                new TestScreen(),
                new EmptyServiceProvider(),
                SessionAccessor,
                CreateDescriptors(),
                access,
                AuthenticationService,
                new NoopModbusRuntimeService(),
                new StaticModbusOptionsProvider(),
                NullLogger<WorkspaceViewModel>.Instance,
                (_, _) =>
                {
                    LogoutCallbackCount++;
                    return Task.CompletedTask;
                });
        }

        private IReadOnlyList<WorkspaceTabDescriptor> CreateDescriptors() =>
        [
            new(
                "route-map",
                "Route Map",
                Permission.ViewRouteMap,
                null,
                _ =>
                {
                    RouteMapFactoryCalls++;
                    return CreateContent();
                },
                0),
            new(
                "signal-map",
                "SignalId ↔ Modbus",
                Permission.ViewSignalMapping,
                null,
                _ =>
                {
                    SignalMappingFactoryCalls++;
                    return CreateContent();
                },
                10),
            new(
                "modbus-demo",
                "Modbus Demo",
                Permission.ViewModbusDiagnostics,
                null,
                _ =>
                {
                    ModbusDemoFactoryCalls++;
                    return CreateContent();
                },
                20),
        ];

        private DisposableContent CreateContent()
        {
            var content = new DisposableContent();
            CreatedContent.Add(content);
            return content;
        }
    }

    private sealed class DisposableContent : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }

    private sealed class TestScreen : IScreen
    {
        public RoutingState Router { get; } = new();
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class TestSessionAccessor : IUserSessionAccessor
    {
        public UserSessionSnapshot Current { get; private set; } = UserSessionSnapshot.Anonymous;

        public void SetCurrent(UserSession session) =>
            Current = UserSessionSnapshot.Authenticated(session);

        public void Clear() => Current = UserSessionSnapshot.Anonymous;
    }

    private sealed class FakeAuthenticationService : IAuthenticationService
    {
        public TestSessionAccessor? SessionAccessor { get; set; }
        public int SignOutCount { get; private set; }

        public Task<AuthenticationResult> AuthenticateAsync(
            AuthenticationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthenticationResult.InvalidCredentials());

        public Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            SignOutCount++;
            SessionAccessor?.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class StaticModbusOptionsProvider : IModbusDemoOptionsProvider
    {
        public ModbusOptions CurrentValue { get; } = new() { StartupMode = ModbusRunMode.None };
    }

    private sealed class NoopModbusRuntimeService : IModbusRuntimeService
    {
        public ModbusStatus Status => throw new NotSupportedException();
        public ModbusSnapshot ClientSnapshot => throw new NotSupportedException();
        public ModbusSnapshot ServerSnapshot => throw new NotSupportedException();
        public ModbusOptions CurrentOptions => new();
        public event EventHandler<ModbusStatus>? StatusChanged { add { } remove { } }
        public event EventHandler<ModbusSnapshot>? SnapshotChanged { add { } remove { } }
        public Task StartAsync(ModbusRunMode mode = ModbusRunMode.Both, ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestartAsync(ModbusRunMode mode = ModbusRunMode.Both, ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopClientAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopServerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
