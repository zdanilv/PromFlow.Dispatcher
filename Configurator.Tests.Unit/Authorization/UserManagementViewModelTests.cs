using Configurator.Application.Services.Authorization;
using Configurator.Desktop.Workspace.Users;
using System.Reactive.Threading.Tasks;
using Xunit;

namespace Configurator.Tests.Unit.Authorization;

public sealed class UserManagementViewModelTests
{
    [Fact]
    public async Task Initialize_LoadsUsersAndSelectsFirstRow()
    {
        var service = new FakeUserManagementService();
        service.AddUser("admin", UserRole.Administrator);
        using var viewModel = new UserManagementViewModel(service);

        await viewModel.InitializeAsync();

        Assert.Equal(["admin"], viewModel.Users.Select(user => user.Username).ToArray());
        Assert.Equal("admin", viewModel.SelectedUser?.Username);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task CreateUser_SuccessRefreshesListAndClearsPassword()
    {
        var service = new FakeUserManagementService();
        service.AddUser("admin", UserRole.Administrator);
        using var viewModel = new UserManagementViewModel(service)
        {
            NewUsername = "operator",
            NewPassword = "ValidPass123",
            NewRole = UserRole.User
        };

        await viewModel.CreateUserCommand.Execute().ToTask();

        Assert.Equal(["admin", "operator"], viewModel.Users.Select(user => user.Username).ToArray());
        Assert.Equal(string.Empty, viewModel.NewPassword);
        Assert.Equal(string.Empty, viewModel.NewUsername);
        Assert.Equal("User created: operator.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task CreateUser_FailureShowsErrorAndClearsPassword()
    {
        var service = new FakeUserManagementService();
        service.AddUser("operator", UserRole.User);
        using var viewModel = new UserManagementViewModel(service)
        {
            NewUsername = "operator",
            NewPassword = "ValidPass123"
        };

        await viewModel.CreateUserCommand.Execute().ToTask();

        Assert.Equal("User already exists.", viewModel.ErrorMessage);
        Assert.Equal(string.Empty, viewModel.NewPassword);
    }

    [Fact]
    public async Task ChangePassword_UsesSelectedRowVersionAndClearsPassword()
    {
        var service = new FakeUserManagementService();
        var user = service.AddUser("operator", UserRole.User);
        using var viewModel = new UserManagementViewModel(service);
        await viewModel.InitializeAsync();
        viewModel.SelectedUser = viewModel.Users.Single(row => row.Id == user.Id);
        viewModel.SelectedNewPassword = "AnotherPass123";

        await viewModel.ChangePasswordCommand.Execute().ToTask();

        Assert.Equal(user.RowVersion, service.LastChangePasswordRowVersion);
        Assert.Equal(string.Empty, viewModel.SelectedNewPassword);
        Assert.Equal("Password changed for operator.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ChangePassword_RowVersionConflictPromptsRefresh()
    {
        var service = new FakeUserManagementService { ForceRowVersionConflict = true };
        service.AddUser("operator", UserRole.User);
        using var viewModel = new UserManagementViewModel(service);
        await viewModel.InitializeAsync();
        viewModel.SelectedNewPassword = "AnotherPass123";

        await viewModel.ChangePasswordCommand.Execute().ToTask();

        Assert.Equal("User changed elsewhere. Refresh and retry.", viewModel.ErrorMessage);
        Assert.Equal(string.Empty, viewModel.SelectedNewPassword);
    }

    [Fact]
    public async Task EnableAndDisableSelectedUser_UseSelectedRowVersion()
    {
        var service = new FakeUserManagementService();
        var user = service.AddUser("operator", UserRole.User);
        using var viewModel = new UserManagementViewModel(service);
        await viewModel.InitializeAsync();

        await viewModel.DisableSelectedCommand.Execute().ToTask();
        await viewModel.EnableSelectedCommand.Execute().ToTask();

        Assert.Equal(user.Id, service.LastSetEnabledUserId);
        Assert.True(service.LastSetEnabledValue);
        Assert.True(service.LastSetEnabledRowVersion >= user.RowVersion);
        Assert.Equal("User enabled: operator.", viewModel.StatusMessage);
    }

    private sealed class FakeUserManagementService : IUserManagementService
    {
        private readonly List<AppUser> _users = [];

        public bool ForceRowVersionConflict { get; set; }
        public long? LastChangePasswordRowVersion { get; private set; }
        public Guid? LastSetEnabledUserId { get; private set; }
        public bool LastSetEnabledValue { get; private set; }
        public long LastSetEnabledRowVersion { get; private set; }

        public AppUser AddUser(string username, UserRole role)
        {
            var now = DateTimeOffset.UtcNow;
            var user = new AppUser(
                Guid.NewGuid(),
                username,
                AppUser.NormalizeUsername(username),
                "hash",
                role,
                true,
                0,
                null,
                now,
                now,
                now,
                null,
                0);
            _users.Add(user);
            _users.Sort((left, right) => string.Compare(left.NormalizedUsername, right.NormalizedUsername, StringComparison.Ordinal));
            return user;
        }

        public Task<bool> IsBootstrapRequiredAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_users.Count == 0);

        public Task<UserManagementResult> BootstrapAdministratorAsync(
            BootstrapAdministratorRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<UserListResult> ListUsersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(UserListResult.Success(_users.Select(ToSummary).ToArray()));

        public Task<UserManagementResult> CreateUserAsync(
            CreateUserRequest request,
            CancellationToken cancellationToken = default)
        {
            if (_users.Any(user => user.NormalizedUsername == AppUser.NormalizeUsername(request.Username)))
            {
                return Task.FromResult(UserManagementResult.Failure("UserDuplicate", "User already exists."));
            }

            if (request.Password.Length < 8)
            {
                return Task.FromResult(UserManagementResult.Failure("PasswordPolicyViolation", "Password does not satisfy the configured policy."));
            }

            var user = AddUser(request.Username, request.Role) with { IsEnabled = request.IsEnabled };
            var index = _users.FindIndex(candidate => candidate.Id == user.Id);
            _users[index] = user;
            return Task.FromResult(UserManagementResult.Success(user));
        }

        public Task<UserManagementResult> ChangePasswordAsync(
            ChangePasswordRequest request,
            CancellationToken cancellationToken = default)
        {
            LastChangePasswordRowVersion = request.ExpectedRowVersion;
            if (ForceRowVersionConflict)
            {
                return Task.FromResult(UserManagementResult.Failure("UserRowVersionConflict", "User row version conflict."));
            }

            var index = _users.FindIndex(user => user.Id == request.UserId);
            if (index < 0)
            {
                return Task.FromResult(UserManagementResult.Failure("UserNotFound", "User was not found."));
            }

            var now = DateTimeOffset.UtcNow;
            _users[index] = _users[index].WithPasswordHash("new-hash", now) with { RowVersion = _users[index].RowVersion + 1 };
            return Task.FromResult(UserManagementResult.Success(_users[index]));
        }

        public Task<UserManagementResult> SetUserEnabledAsync(
            SetUserEnabledRequest request,
            CancellationToken cancellationToken = default)
        {
            LastSetEnabledUserId = request.UserId;
            LastSetEnabledValue = request.IsEnabled;
            LastSetEnabledRowVersion = request.ExpectedRowVersion;
            var index = _users.FindIndex(user => user.Id == request.UserId);
            if (index < 0)
            {
                return Task.FromResult(UserManagementResult.Failure("UserNotFound", "User was not found."));
            }

            _users[index] = _users[index] with
            {
                IsEnabled = request.IsEnabled,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                RowVersion = _users[index].RowVersion + 1
            };
            return Task.FromResult(UserManagementResult.Success(_users[index]));
        }

        private static UserSummary ToSummary(AppUser user)
            => new(
                user.Id,
                user.Username,
                user.Role,
                user.IsEnabled,
                user.FailedLoginCount,
                user.LockoutUntilUtc,
                user.CreatedAtUtc,
                user.UpdatedAtUtc,
                user.PasswordChangedAtUtc,
                user.LastLoginAtUtc,
                user.RowVersion);
    }
}
