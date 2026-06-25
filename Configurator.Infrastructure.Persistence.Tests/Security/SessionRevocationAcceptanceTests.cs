using Configurator.Application.Services.Authorization;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Security;

public sealed class SessionRevocationAcceptanceTests
{
    [Fact]
    public async Task DisableCurrentUser_ClearsSessionAndNextManageUsersCallIsNotAuthenticated()
    {
        using var fixture = new SecurityTestFixture();
        var service = fixture.CreateUserManagementService();
        var admin = await BootstrapAndSignInAdminAsync(fixture);

        var disabled = await service.SetUserEnabledAsync(
            new SetUserEnabledRequest(admin.Id, IsEnabled: false, admin.RowVersion),
            CancellationToken.None);
        var nextCall = await service.CreateUserAsync(
            new CreateUserRequest("operator", "ValidPass123", UserRole.User),
            CancellationToken.None);

        Assert.True(disabled.Succeeded, disabled.ErrorMessage);
        Assert.False(fixture.SessionAccessor.Current.IsAuthenticated);
        Assert.False(nextCall.Succeeded);
        Assert.Equal("NotAuthenticated", nextCall.ErrorCode);
        Assert.Contains(fixture.AuditService.Records, record =>
            record.EventType == "UserDisabled"
            && record.ActorUserId == admin.Id.ToString("D")
            && record.TargetUserId == admin.Id.ToString("D"));
    }

    [Fact]
    public async Task DisableDifferentUser_KeepsCurrentAdminSession()
    {
        using var fixture = new SecurityTestFixture();
        var service = fixture.CreateUserManagementService();
        var admin = await BootstrapAndSignInAdminAsync(fixture);
        var user = await service.CreateUserAsync(
            new CreateUserRequest("operator", "ValidPass123", UserRole.User),
            CancellationToken.None);
        Assert.True(user.Succeeded, user.ErrorMessage);

        var disabled = await service.SetUserEnabledAsync(
            new SetUserEnabledRequest(user.User!.Id, IsEnabled: false, user.User.RowVersion),
            CancellationToken.None);
        var nextCall = await service.CreateUserAsync(
            new CreateUserRequest("operator2", "ValidPass123", UserRole.User),
            CancellationToken.None);

        Assert.True(disabled.Succeeded, disabled.ErrorMessage);
        Assert.True(fixture.SessionAccessor.Current.IsAuthenticated);
        Assert.Equal(admin.Id, fixture.SessionAccessor.Current.Session!.UserId);
        Assert.True(nextCall.Succeeded, nextCall.ErrorMessage);
        Assert.Contains(fixture.AuditService.Records, record =>
            record.EventType == "UserDisabled"
            && record.ActorUserId == admin.Id.ToString("D")
            && record.TargetUserId == user.User.Id.ToString("D"));
    }

    [Fact]
    public async Task DisableCurrentUser_ClearsSessionEvenWhenAuditFails()
    {
        using var fixture = new SecurityTestFixture();
        var service = fixture.CreateUserManagementService();
        var admin = await BootstrapAndSignInAdminAsync(fixture);
        fixture.AuditService.Fail = true;

        var disabled = await service.SetUserEnabledAsync(
            new SetUserEnabledRequest(admin.Id, IsEnabled: false, admin.RowVersion),
            CancellationToken.None);

        Assert.True(disabled.Succeeded, disabled.ErrorMessage);
        Assert.False(fixture.SessionAccessor.Current.IsAuthenticated);
    }

    private static async Task<AppUser> BootstrapAndSignInAdminAsync(SecurityTestFixture fixture)
    {
        var bootstrap = await fixture
            .CreateUserManagementService()
            .BootstrapAdministratorAsync(
                new BootstrapAdministratorRequest("admin", "ValidPass123"),
                CancellationToken.None);
        Assert.True(bootstrap.Succeeded, bootstrap.ErrorMessage);

        var login = await fixture
            .CreateAuthenticationService()
            .AuthenticateAsync(new AuthenticationRequest("admin", "ValidPass123"), CancellationToken.None);
        Assert.True(login.Succeeded, login.ErrorMessage);

        var fresh = await fixture.UserRepository
            .FindByNormalizedUsernameAsync(AppUser.NormalizeUsername("admin"), CancellationToken.None);
        Assert.NotNull(fresh);

        return fresh;
    }
}
