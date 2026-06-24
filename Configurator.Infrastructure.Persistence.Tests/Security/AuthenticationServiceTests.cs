using Configurator.Application.Services.Authorization;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Security;

public sealed class AuthenticationServiceTests
{
    [Fact]
    public async Task Authenticate_WithoutUsers_ReturnsBootstrapRequired()
    {
        using var fixture = new SecurityTestFixture();

        var result = await fixture.CreateAuthenticationService().AuthenticateAsync(
            new AuthenticationRequest("operator", "ValidPass123"),
            CancellationToken.None);

        Assert.Equal(AuthenticationResultStatus.BootstrapRequired, result.Status);
        Assert.False(fixture.SessionAccessor.Current.IsAuthenticated);
    }

    [Fact]
    public async Task Authenticate_SuccessCreatesSessionAndSignOutClearsIt()
    {
        using var fixture = new SecurityTestFixture();
        await BootstrapAsync(fixture);

        var login = await fixture.CreateAuthenticationService().AuthenticateAsync(
            new AuthenticationRequest("admin", "ValidPass123"),
            CancellationToken.None);
        await fixture.CreateAuthenticationService().SignOutAsync(CancellationToken.None);

        Assert.True(login.Succeeded, login.ErrorMessage);
        Assert.NotNull(login.Session);
        Assert.Contains(Permission.ManageUsers, login.Session!.Permissions);
        Assert.False(fixture.SessionAccessor.Current.IsAuthenticated);
        Assert.Contains(fixture.AuditService.Records, record => record.EventType == "AuthenticationSucceeded");
        Assert.Contains(fixture.AuditService.Records, record => record.EventType == "SignOut");
    }

    [Fact]
    public async Task Authenticate_WrongInputLocksOutAndExpiredLockoutCanSucceed()
    {
        using var fixture = new SecurityTestFixture();
        await BootstrapAsync(fixture);
        var service = fixture.CreateAuthenticationService();

        var first = await service.AuthenticateAsync(new AuthenticationRequest("admin", "WrongPass123"), CancellationToken.None);
        var second = await service.AuthenticateAsync(new AuthenticationRequest("admin", "WrongPass123"), CancellationToken.None);
        var locked = await service.AuthenticateAsync(new AuthenticationRequest("admin", "ValidPass123"), CancellationToken.None);
        var user = await fixture.UserRepository.FindByNormalizedUsernameAsync(AppUser.NormalizeUsername("admin"), CancellationToken.None);
        var expired = user! with
        {
            LockoutUntilUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await fixture.UserRepository.UpdateAsync(expired, user.RowVersion, CancellationToken.None);
        var success = await service.AuthenticateAsync(new AuthenticationRequest("admin", "ValidPass123"), CancellationToken.None);

        Assert.Equal(AuthenticationResultStatus.InvalidCredentials, first.Status);
        Assert.Equal(AuthenticationResultStatus.LockedOut, second.Status);
        Assert.Equal(AuthenticationResultStatus.LockedOut, locked.Status);
        Assert.True(success.Succeeded, success.ErrorMessage);
    }

    [Fact]
    public async Task Authenticate_DisabledUserReturnsGenericInvalidCredentials()
    {
        using var fixture = new SecurityTestFixture();
        var admin = await BootstrapAsync(fixture);
        var disabled = admin with { IsEnabled = false, UpdatedAtUtc = DateTimeOffset.UtcNow };
        await fixture.UserRepository.UpdateAsync(disabled, admin.RowVersion, CancellationToken.None);

        var result = await fixture.CreateAuthenticationService().AuthenticateAsync(
            new AuthenticationRequest("admin", "ValidPass123"),
            CancellationToken.None);

        Assert.Equal(AuthenticationResultStatus.InvalidCredentials, result.Status);
        Assert.False(fixture.SessionAccessor.Current.IsAuthenticated);
        Assert.Contains(fixture.AuditService.Records, record => record.ReasonCode == "UserDisabled");
    }

    private static async Task<AppUser> BootstrapAsync(SecurityTestFixture fixture)
    {
        var result = await fixture.CreateUserManagementService().BootstrapAdministratorAsync(
            new BootstrapAdministratorRequest("admin", "ValidPass123"),
            CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);

        return result.User!;
    }
}
