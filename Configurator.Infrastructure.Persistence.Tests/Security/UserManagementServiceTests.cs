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
}
