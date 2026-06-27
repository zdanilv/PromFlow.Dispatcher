using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Licensing;
using Configurator.Application.Services.Runtime;
using Configurator.Desktop.Main;
using Configurator.Desktop.Workspace;
using Configurator.Desktop.Workspace.Authorization;
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

        Assert.Equal(1, services.RuntimeCoordinator.StartCount);
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

    [Fact]
    public async Task LoginSuccessWithRememberPasswordSavesEncryptedPreferencesAndClearsPassword()
    {
        var auth = new FakeAuthenticationService();
        var store = new RecordingLoginCredentialStore();
        var successCount = 0;
        using var viewModel = new AuthorizationViewModel(
            new TestScreen(),
            auth,
            _ =>
            {
                successCount++;
                return Task.CompletedTask;
            },
            store)
        {
            Username = "operator",
            Password = "ValidPass123",
            RememberPassword = true,
            AutoLogin = true
        };

        await viewModel.AuthenticateCommand.Execute().ToTask();

        Assert.Equal(1, successCount);
        Assert.Equal("operator", store.SavedPreferences?.Username);
        Assert.Equal("ValidPass123", store.SavedPreferences?.Password);
        Assert.True(store.SavedPreferences?.RememberPassword);
        Assert.True(store.SavedPreferences?.AutoLogin);
        Assert.Empty(viewModel.Password);
    }

    [Fact]
    public async Task InitializeWithAutoLoginUsesSavedCredentialsOnce()
    {
        var auth = new FakeAuthenticationService();
        var store = new RecordingLoginCredentialStore
        {
            Preferences = new LoginCredentialPreferences("operator", "ValidPass123", true, true)
        };
        var successCount = 0;
        using var viewModel = new AuthorizationViewModel(
            new TestScreen(),
            auth,
            _ =>
            {
                successCount++;
                return Task.CompletedTask;
            },
            store);

        await viewModel.InitializeAsync();
        await viewModel.TryAutoLoginAsync();

        Assert.Equal(1, successCount);
        Assert.Equal("operator", viewModel.Username);
        Assert.True(viewModel.RememberPassword);
        Assert.True(viewModel.AutoLogin);
        Assert.Empty(viewModel.Password);
    }

    [Fact]
    public async Task FailedAutoLoginClearsStoredCredentialsAndStaysOnLogin()
    {
        var auth = new FakeAuthenticationService { Result = AuthenticationResult.InvalidCredentials() };
        var store = new RecordingLoginCredentialStore
        {
            Preferences = new LoginCredentialPreferences("operator", "stale", true, true)
        };
        using var viewModel = new AuthorizationViewModel(
            new TestScreen(),
            auth,
            _ => throw new InvalidOperationException("Should not navigate."),
            store);
        using var interaction = viewModel.ErrorInteraction.RegisterHandler(context =>
        {
            context.SetOutput(System.Reactive.Unit.Default);
        });

        await viewModel.InitializeAsync();

        Assert.Equal("Invalid username or password.", viewModel.ErrorMessage);
        Assert.Equal(1, store.ClearCount);
        Assert.False(viewModel.RememberPassword);
        Assert.False(viewModel.AutoLogin);
        Assert.Empty(viewModel.Password);
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

        public FakeRuntimeCoordinator RuntimeCoordinator { get; } = new();

        public MainViewModel CreateMainViewModel() =>
            new(
                _userManagementService,
                _licenseService,
                RuntimeCoordinator,
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
                onLogout);
        }
    }

    private sealed class FakeRuntimeCoordinator : IApplicationRuntimeCoordinator
    {
        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            return Task.CompletedTask;
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

    private sealed class RecordingLoginCredentialStore : ILoginCredentialStore
    {
        public LoginCredentialPreferences Preferences { get; set; } = LoginCredentialPreferences.Empty;

        public LoginCredentialPreferences? SavedPreferences { get; private set; }

        public int ClearCount { get; private set; }

        public Task<LoginCredentialPreferences> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Preferences);

        public Task SaveAsync(LoginCredentialPreferences preferences, CancellationToken cancellationToken = default)
        {
            SavedPreferences = preferences;
            Preferences = preferences;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            ClearCount++;
            Preferences = LoginCredentialPreferences.Empty;
            return Task.CompletedTask;
        }
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

        public Task<UserListResult> ListUsersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(UserListResult.Success([]));

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
