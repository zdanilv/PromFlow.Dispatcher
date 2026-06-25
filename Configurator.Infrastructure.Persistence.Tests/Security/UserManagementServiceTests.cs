using Configurator.Application.Services.Authorization;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Security;

public sealed class UserManagementServiceTests
{
    [Fact]
    public async Task BootstrapAdministrator_RequiresPolicyAndCanRunOnce()
    {
        using var fixture = new SecurityTestFixture();
        var service = fixture.CreateUserManagementService();

        var weak = await service.BootstrapAdministratorAsync(
            new BootstrapAdministratorRequest("admin", "weak"),
            CancellationToken.None);
        var first = await service.BootstrapAdministratorAsync(
            new BootstrapAdministratorRequest("admin", "ValidPass123"),
            CancellationToken.None);
        var second = await service.BootstrapAdministratorAsync(
            new BootstrapAdministratorRequest("other", "ValidPass123"),
            CancellationToken.None);

        Assert.False(weak.Succeeded);
        Assert.Equal("PasswordPolicyViolation", weak.ErrorCode);
        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.False(second.Succeeded);
        Assert.Equal("BootstrapAlreadyCompleted", second.ErrorCode);
    }

    [Fact]
    public async Task CreateUser_RequiresManageUsersPermission()
    {
        using var fixture = new SecurityTestFixture();
        var service = fixture.CreateUserManagementService();
        var admin = await service.BootstrapAdministratorAsync(
            new BootstrapAdministratorRequest("admin", "ValidPass123"),
            CancellationToken.None);
        var denied = await service.CreateUserAsync(
            new CreateUserRequest("operator", "ValidPass123", UserRole.User),
            CancellationToken.None);
        fixture.SessionAccessor.SetCurrent(new UserSession(
            Guid.NewGuid(),
            admin.User!.Id,
            admin.User.Username,
            admin.User.Role,
            fixture.CreateAuthorizationService().GetPermissions(admin.User.Role),
            DateTimeOffset.UtcNow));
        var allowed = await service.CreateUserAsync(
            new CreateUserRequest("operator", "ValidPass123", UserRole.User),
            CancellationToken.None);

        Assert.False(denied.Succeeded);
        Assert.Equal("NotAuthenticated", denied.ErrorCode);
        Assert.True(allowed.Succeeded, allowed.ErrorMessage);
        Assert.Equal(UserRole.User, allowed.User!.Role);
    }

    [Fact]
    public async Task ListUsers_RequiresManageUsersAndReturnsSummariesWithoutPasswordHash()
    {
        using var fixture = new SecurityTestFixture();
        var service = fixture.CreateUserManagementService();
        var admin = await service.BootstrapAdministratorAsync(
            new BootstrapAdministratorRequest("admin", "ValidPass123"),
            CancellationToken.None);
        var denied = await service.ListUsersAsync(CancellationToken.None);
        fixture.SessionAccessor.SetCurrent(new UserSession(
            Guid.NewGuid(),
            admin.User!.Id,
            admin.User.Username,
            admin.User.Role,
            fixture.CreateAuthorizationService().GetPermissions(admin.User.Role),
            DateTimeOffset.UtcNow));
        await service.CreateUserAsync(
            new CreateUserRequest("operator", "ValidPass123", UserRole.User),
            CancellationToken.None);

        var allowed = await service.ListUsersAsync(CancellationToken.None);

        Assert.False(denied.Succeeded);
        Assert.Equal("NotAuthenticated", denied.ErrorCode);
        Assert.True(allowed.Succeeded, allowed.ErrorMessage);
        Assert.Equal(["admin", "operator"], allowed.Users.Select(user => user.Username).ToArray());
        Assert.DoesNotContain(allowed.Users, user => user.GetType().GetProperty("PasswordHash") is not null);
    }

    [Fact]
    public async Task ListUsers_ReflectsPasswordAndEnabledStateChanges()
    {
        using var fixture = new SecurityTestFixture();
        var service = fixture.CreateUserManagementService();
        var admin = await service.BootstrapAdministratorAsync(
            new BootstrapAdministratorRequest("admin", "ValidPass123"),
            CancellationToken.None);
        fixture.SessionAccessor.SetCurrent(new UserSession(
            Guid.NewGuid(),
            admin.User!.Id,
            admin.User.Username,
            admin.User.Role,
            fixture.CreateAuthorizationService().GetPermissions(admin.User.Role),
            DateTimeOffset.UtcNow));
        var created = await service.CreateUserAsync(
            new CreateUserRequest("operator", "ValidPass123", UserRole.User),
            CancellationToken.None);
        var changed = await service.ChangePasswordAsync(
            new ChangePasswordRequest(created.User!.Id, "AnotherPass123", created.User.RowVersion),
            CancellationToken.None);
        await service.SetUserEnabledAsync(
            new SetUserEnabledRequest(created.User.Id, false, changed.User!.RowVersion),
            CancellationToken.None);

        var users = await service.ListUsersAsync(CancellationToken.None);
        var user = Assert.Single(users.Users, summary => summary.Username == "operator");

        Assert.False(user.IsEnabled);
        Assert.True(user.PasswordChangedAtUtc >= created.User.PasswordChangedAtUtc);
        Assert.True(user.RowVersion > created.User.RowVersion);
    }

    [Fact]
    public void UserRolePermissions_AreLimitedToRouteMapAndCommands()
    {
        using var fixture = new SecurityTestFixture();
        var permissions = fixture.CreateAuthorizationService().GetPermissions(UserRole.User);

        Assert.Equal([Permission.ViewRouteMap, Permission.IssueEquipmentCommands], permissions);
        Assert.DoesNotContain(Permission.ViewSignalMapping, permissions);
        Assert.DoesNotContain(Permission.ViewModbusDiagnostics, permissions);
        Assert.DoesNotContain(Permission.ViewLicense, permissions);
        Assert.DoesNotContain(Permission.ManageUsers, permissions);
    }
}
