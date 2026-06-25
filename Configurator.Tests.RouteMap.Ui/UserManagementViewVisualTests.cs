using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Configurator.Application.Services.Authorization;
using Configurator.Desktop.Workspace.Users;
using Xunit;

namespace Configurator.Tests.RouteMap.Ui;

public sealed class UserManagementViewVisualTests
{
    [AvaloniaFact]
    public void UserManagementView_RendersListAndActions()
    {
        using var viewModel = new UserManagementViewModel(new FakeUserManagementService());
        var view = new UserManagementView { DataContext = viewModel };
        var window = new Window { Width = 1100, Height = 700, Content = view };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
        Assert.Contains("Users", texts);
        Assert.Contains("admin", texts);
        Assert.Contains("Create user", texts);
        Assert.Contains("Selected user", texts);
        Assert.True(view.GetVisualDescendants().OfType<Button>().Count() >= 5);
        window.Close();
    }

    private sealed class FakeUserManagementService : IUserManagementService
    {
        private readonly AppUser _admin;

        public FakeUserManagementService()
        {
            var now = DateTimeOffset.UtcNow;
            _admin = new AppUser(
                Guid.NewGuid(),
                "admin",
                AppUser.NormalizeUsername("admin"),
                "hash",
                UserRole.Administrator,
                true,
                0,
                null,
                now,
                now,
                now,
                null,
                0);
        }

        public Task<bool> IsBootstrapRequiredAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<UserManagementResult> BootstrapAdministratorAsync(
            BootstrapAdministratorRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<UserListResult> ListUsersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(UserListResult.Success([ToSummary(_admin)]));

        public Task<UserManagementResult> CreateUserAsync(
            CreateUserRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<UserManagementResult> ChangePasswordAsync(
            ChangePasswordRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<UserManagementResult> SetUserEnabledAsync(
            SetUserEnabledRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
