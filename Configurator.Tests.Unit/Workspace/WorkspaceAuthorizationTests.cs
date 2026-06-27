using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Licensing;
using Configurator.Desktop.Workspace;
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
        Assert.Equal(0, fixture.ModbusTcpFactoryCalls);
        Assert.Equal(0, fixture.ModbusDemoFactoryCalls);
        Assert.Equal(0, fixture.UsersFactoryCalls);
    }

    [Fact]
    public async Task AdministratorSessionCreatesAllExistingTabs()
    {
        var fixture = new WorkspaceFixture(Enum.GetValues<Permission>());
        using var workspace = fixture.CreateWorkspace();

        await workspace.InitializeAsync();

        Assert.Equal(
            ["Route Map", "SignalId ↔ Modbus", "Modbus TCP", "Modbus Demo", "Archive", "License", "Users"],
            workspace.Tabs.Select(tab => tab.Header).ToArray());
        Assert.Equal(1, fixture.RouteMapFactoryCalls);
        Assert.Equal(1, fixture.SignalMappingFactoryCalls);
        Assert.Equal(1, fixture.ModbusTcpFactoryCalls);
        Assert.Equal(1, fixture.ModbusDemoFactoryCalls);
        Assert.Equal(1, fixture.ArchiveFactoryCalls);
        Assert.Equal(1, fixture.LicenseFactoryCalls);
        Assert.Equal(1, fixture.UsersFactoryCalls);
    }

    [Fact]
    public async Task UserWithoutLicensedRouteMapGetsFallbackAndNoFactories()
    {
        var fixture = new WorkspaceFixture(
            Permission.ViewRouteMap,
            Permission.IssueEquipmentCommands);
        fixture.LicenseStateAccessor.Current = LicenseState.Missing(DateTimeOffset.UtcNow);
        using var workspace = fixture.CreateWorkspace();

        await workspace.InitializeAsync();

        Assert.Empty(workspace.Tabs);
        Assert.True(workspace.HasNoTabs);
        Assert.NotNull(workspace.AccessUnavailable);
        Assert.Equal(0, fixture.RouteMapFactoryCalls);
    }

    [Fact]
    public async Task AdministratorWithoutCommercialLicenseCanOpenRecoveryTabsOnly()
    {
        var fixture = new WorkspaceFixture(Enum.GetValues<Permission>());
        fixture.LicenseStateAccessor.Current = LicenseState.Missing(DateTimeOffset.UtcNow);
        using var workspace = fixture.CreateWorkspace();

        await workspace.InitializeAsync();

        Assert.Equal(["License", "Users"], workspace.Tabs.Select(tab => tab.Header).ToArray());
        Assert.Equal(0, fixture.RouteMapFactoryCalls);
        Assert.Equal(1, fixture.LicenseFactoryCalls);
        Assert.Equal(1, fixture.UsersFactoryCalls);
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
            LicenseStateAccessor.Current = ValidState(
                LicenseFeature.RouteMap,
                LicenseFeature.RemoteControl,
                LicenseFeature.EngineeringTools,
                LicenseFeature.Diagnostics,
                LicenseFeature.Archive,
                LicenseFeature.ArchiveExport);
            SessionAccessor.SetCurrent(new UserSession(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "operator",
                UserRole.User,
                _permissions,
                DateTimeOffset.UtcNow));
        }

        public TestSessionAccessor SessionAccessor { get; } = new();
        public TestLicenseStateAccessor LicenseStateAccessor { get; } = new();
        public FakeAuthenticationService AuthenticationService { get; } = new();
        public FakeLicenseService LicenseService { get; private set; } = null!;
        public int RouteMapFactoryCalls { get; private set; }
        public int SignalMappingFactoryCalls { get; private set; }
        public int ModbusTcpFactoryCalls { get; private set; }
        public int ModbusDemoFactoryCalls { get; private set; }
        public int ArchiveFactoryCalls { get; private set; }
        public int LicenseFactoryCalls { get; private set; }
        public int UsersFactoryCalls { get; private set; }
        public int LogoutCallbackCount { get; private set; }
        public List<DisposableContent> CreatedContent { get; } = [];

        public WorkspaceViewModel CreateWorkspace()
        {
            AuthenticationService.SessionAccessor = SessionAccessor;
            LicenseService = new FakeLicenseService(LicenseStateAccessor);
            var access = new DefaultAccessDecisionService(SessionAccessor, new LicenseFeatureGate(LicenseStateAccessor));
            return new WorkspaceViewModel(
                new TestScreen(),
                new EmptyServiceProvider(),
                SessionAccessor,
                CreateDescriptors(),
                access,
                AuthenticationService,
                LicenseService,
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
                LicenseFeature.RouteMap,
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
                LicenseFeature.EngineeringTools,
                _ =>
                {
                    SignalMappingFactoryCalls++;
                    return CreateContent();
                },
                10),
            new(
                "modbus-tcp",
                "Modbus TCP",
                Permission.ConfigureModbus,
                LicenseFeature.Diagnostics,
                _ =>
                {
                    ModbusTcpFactoryCalls++;
                    return CreateContent();
                },
                15),
            new(
                "modbus-demo",
                "Modbus Demo",
                Permission.ViewModbusDiagnostics,
                LicenseFeature.Diagnostics,
                _ =>
                {
                    ModbusDemoFactoryCalls++;
                    return CreateContent();
                },
                20),
            new(
                "archive",
                "Archive",
                Permission.ViewArchive,
                LicenseFeature.Archive,
                _ =>
                {
                    ArchiveFactoryCalls++;
                    return CreateContent();
                },
                25),
            new(
                "license",
                "License",
                Permission.ViewLicense,
                null,
                _ =>
                {
                    LicenseFactoryCalls++;
                    return CreateContent();
                },
                30),
            new(
                "users",
                "Users",
                Permission.ManageUsers,
                null,
                _ =>
                {
                    UsersFactoryCalls++;
                    return CreateContent();
                },
                40),
        ];

        private DisposableContent CreateContent()
        {
            var content = new DisposableContent();
            CreatedContent.Add(content);
            return content;
        }

        private static LicenseState ValidState(params string[] features)
        {
            var payload = new LicensePayload
            {
                LicenseId = Guid.NewGuid(),
                Product = LicenseConstants.Product,
                IssuedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
                ValidFromUtc = DateTimeOffset.UtcNow.AddDays(-1),
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
                Edition = LicenseEdition.Professional,
                LicenseVersion = LicenseConstants.LicenseVersion,
                ProductVersion = new LicenseProductVersionRange(),
                Features = [.. features],
                Installation = new LicenseInstallationProfile
                {
                    BindingMode = LicenseInstallationBindingMode.InstallationId,
                    InstallationId = "installation-1"
                }
            };

            return new LicenseState(LicenseStatus.Valid, DateTimeOffset.UtcNow, payload, []);
        }
    }

    private sealed class TestLicenseStateAccessor : ILicenseStateAccessor
    {
        public LicenseState Current { get; set; } = LicenseState.Missing(DateTimeOffset.UnixEpoch);
        public event EventHandler<LicenseStateChangedEventArgs>? StateChanged;

        public void RaiseChanged(LicenseState previous, LicenseState current)
        {
            Current = current;
            StateChanged?.Invoke(this, new LicenseStateChangedEventArgs(previous, current));
        }
    }

    private sealed class FakeLicenseService(TestLicenseStateAccessor accessor) : ILicenseService
    {
        public int RefreshCount { get; private set; }

        public Task<LicenseState> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            RefreshAsync(cancellationToken);

        public Task<LicenseState> RefreshAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshCount++;
            return Task.FromResult(accessor.Current);
        }

        public Task<LicenseInstallResult> InstallAsync(
            LicenseInstallRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(LicenseInstallResult.Failure(
                accessor.Current,
                LicenseValidationErrorCode.StoreUnavailable,
                null,
                null,
                "Not supported."));

        public Task<LicenseValidationResult> VerifyAsync(
            byte[] licenseBytes,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(LicenseValidationResult.Failure(
                LicenseStatus.Invalid,
                LicenseValidationErrorCode.InvalidJson,
                "Not supported."));
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

        public bool ClearIfCurrent(Guid userId)
        {
            if (userId == Guid.Empty)
            {
                throw new ArgumentException("User id must not be empty.", nameof(userId));
            }

            if (Current.Session?.UserId != userId)
            {
                return false;
            }

            Current = UserSessionSnapshot.Anonymous;
            return true;
        }
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
}
