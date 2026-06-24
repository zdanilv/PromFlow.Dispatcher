using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Licensing;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Desktop.Main;
using Configurator.Desktop.Workspace;
using Configurator.Desktop.Workspace.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using ReactiveUI;
using System.Reactive;
using System.Reactive.Threading.Tasks;
using Xunit;

namespace Configurator.Tests.Unit.Authorization;

public sealed class LoginFlowViewModelTests
{
    [Fact]
    public async Task MainViewModel_StartsAtBootstrapWhenNoUsersExist()
    {
        var services = new FlowServices { BootstrapRequired = true };
        using var main = services.CreateMainViewModel();

        await main.StartupTask;

        Assert.IsType<AdminBootstrapViewModel>(main.CurrentViewModel);
    }

    [Fact]
    public async Task MainViewModel_StartsAtLoginWhenUsersExist()
    {
        var services = new FlowServices { BootstrapRequired = false };
        using var main = services.CreateMainViewModel();

        await main.StartupTask;

        Assert.IsType<AuthorizationViewModel>(main.CurrentViewModel);
    }

    [Fact]
    public async Task BootstrapSuccessClearsPasswordsAndRoutesToLogin()
    {
        var services = new FlowServices { BootstrapRequired = true };
        using var main = services.CreateMainViewModel();
        await main.StartupTask;
        var bootstrap = Assert.IsType<AdminBootstrapViewModel>(main.CurrentViewModel);

        bootstrap.Username = "admin";
        bootstrap.Password = "ValidPass123";
        bootstrap.ConfirmPassword = "ValidPass123";
        await bootstrap.BootstrapCommand.Execute().ToTask();

        Assert.Empty(bootstrap.Password);
        Assert.Empty(bootstrap.ConfirmPassword);
        Assert.IsType<AuthorizationViewModel>(main.CurrentViewModel);
    }

    [Fact]
    public async Task LoginFailureUsesGenericErrorAndClearsPassword()
    {
        var auth = new FakeAuthenticationService { Result = AuthenticationResult.InvalidCredentials() };
        using var viewModel = new AuthorizationViewModel(
            new TestScreen(),
            auth,
            _ => throw new InvalidOperationException("Should not navigate."));
        using var interaction = viewModel.ErrorInteraction.RegisterHandler(context =>
        {
            context.SetOutput(System.Reactive.Unit.Default);
        });
        viewModel.Username = "operator";
        viewModel.Password = "wrong";

        await viewModel.AuthenticateCommand.Execute().ToTask();

        Assert.Equal("Invalid username or password.", viewModel.ErrorMessage);
        Assert.Empty(viewModel.Password);
    }

    [Fact]
    public async Task LoginSuccessCreatesWorkspaceOnlyAfterAuthentication()
    {
        var services = new FlowServices { BootstrapRequired = false };
        using var main = services.CreateMainViewModel();
        await main.StartupTask;
        Assert.Equal(0, services.WorkspaceCreateCount);
        var login = Assert.IsType<AuthorizationViewModel>(main.CurrentViewModel);

        login.Username = "operator";
        login.Password = "ValidPass123";
        await login.AuthenticateCommand.Execute().ToTask();

        var workspace = Assert.IsType<WorkspaceViewModel>(main.CurrentViewModel);
        Assert.Equal(1, services.WorkspaceCreateCount);
        Assert.Equal(["Route Map"], workspace.Tabs.Select(tab => tab.Header).ToArray());
        Assert.Empty(login.Password);
    }

    private sealed class FlowServices
    {
        private readonly TestSessionAccessor _sessionAccessor = new();
        private readonly FakeAuthenticationService _authenticationService;
        private readonly FakeLicenseService _licenseService = new();
        private readonly FakeUserManagementService _userManagementService = new();

        public FlowServices()
        {
            _authenticationService = new FakeAuthenticationService(_sessionAccessor);
        }

        public bool BootstrapRequired
        {
            get => _userManagementService.BootstrapRequired;
            set => _userManagementService.BootstrapRequired = value;
        }

        public int WorkspaceCreateCount { get; private set; }

        public MainViewModel CreateMainViewModel() =>
            new(
                _userManagementService,
                _licenseService,
                (screen, onSucceeded) => new AuthorizationViewModel(screen, _authenticationService, onSucceeded),
                (screen, onSucceeded) => new AdminBootstrapViewModel(screen, _userManagementService, onSucceeded),
                (screen, onLogout) =>
                {
                    WorkspaceCreateCount++;
                    return CreateWorkspace(screen, onLogout);
                });

        private WorkspaceViewModel CreateWorkspace(
            IScreen screen,
            Func<WorkspaceViewModel, CancellationToken, Task> onLogout)
        {
            var descriptors = new[]
            {
                new WorkspaceTabDescriptor(
                    "route-map",
                    "Route Map",
                    Permission.ViewRouteMap,
                    null,
                    _ => new DisposableContent(),
                    0)
            };
            return new WorkspaceViewModel(
                screen,
                new EmptyServiceProvider(),
                _sessionAccessor,
                descriptors,
                new FixedAccessDecisionService(allow: true),
                _authenticationService,
                _licenseService,
                new NoopModbusRuntimeService(),
                new StaticModbusOptionsProvider(),
                NullLogger<WorkspaceViewModel>.Instance,
                onLogout);
        }
    }

    private sealed class FakeLicenseService : ILicenseService
    {
        public int RefreshCount { get; private set; }

        public Task<LicenseState> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            RefreshAsync(cancellationToken);

        public Task<LicenseState> RefreshAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshCount++;
            return Task.FromResult(LicenseState.Missing(DateTimeOffset.UnixEpoch));
        }

        public Task<LicenseInstallResult> InstallAsync(
            LicenseInstallRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(LicenseInstallResult.Failure(
                LicenseState.Missing(DateTimeOffset.UnixEpoch),
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

    private sealed class TestScreen : IScreen
    {
        public RoutingState Router { get; } = new();
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class DisposableContent : IDisposable
    {
        public void Dispose() { }
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
        private readonly TestSessionAccessor? _sessionAccessor;

        public FakeAuthenticationService(TestSessionAccessor? sessionAccessor = null)
        {
            _sessionAccessor = sessionAccessor;
            Result = AuthenticationResult.Success(CreateSession());
        }

        public AuthenticationResult Result { get; set; }

        public int SignOutCount { get; private set; }

        public Task<AuthenticationResult> AuthenticateAsync(
            AuthenticationRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Result.Succeeded && Result.Session is not null)
            {
                _sessionAccessor?.SetCurrent(Result.Session);
            }

            return Task.FromResult(Result);
        }

        public Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            SignOutCount++;
            _sessionAccessor?.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUserManagementService : IUserManagementService
    {
        public bool BootstrapRequired { get; set; }

        public Task<bool> IsBootstrapRequiredAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(BootstrapRequired);

        public Task<UserManagementResult> BootstrapAdministratorAsync(
            BootstrapAdministratorRequest request,
            CancellationToken cancellationToken = default)
        {
            BootstrapRequired = false;
            return Task.FromResult(UserManagementResult.Success(CreateUser(request.Username, UserRole.Administrator)));
        }

        public Task<UserManagementResult> CreateUserAsync(
            CreateUserRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(UserManagementResult.Success(CreateUser(request.Username, request.Role)));

        public Task<UserManagementResult> ChangePasswordAsync(
            ChangePasswordRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(UserManagementResult.Failure("NotSupported", "Not supported."));

        public Task<UserManagementResult> SetUserEnabledAsync(
            SetUserEnabledRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(UserManagementResult.Failure("NotSupported", "Not supported."));
    }

    private sealed class FixedAccessDecisionService(bool allow) : IAccessDecisionService
    {
        public AccessDecision Authorize(AccessRequirement requirement) =>
            allow
                ? AccessDecision.Allow(requirement, CreateSession())
                : AccessDecision.Deny(requirement, "PermissionDenied", CreateSession());

        public Task<AccessDecision> AuthorizeAsync(
            AccessRequirement requirement,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Authorize(requirement));
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

    private static UserSession CreateSession() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "operator",
            UserRole.User,
            [Permission.ViewRouteMap, Permission.IssueEquipmentCommands],
            DateTimeOffset.UtcNow);

    private static AppUser CreateUser(string username, UserRole role)
    {
        var now = DateTimeOffset.UtcNow;
        return new AppUser(
            Guid.NewGuid(),
            username,
            AppUser.NormalizeUsername(username),
            "hash",
            role,
            isEnabled: true,
            failedLoginCount: 0,
            lockoutUntilUtc: null,
            now,
            now,
            now,
            lastLoginAtUtc: null,
            rowVersion: 0);
    }
}
