using Configurator.Application.Services.Authorization;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Security;

public sealed class PasswordHashServiceTests
{
    [Fact]
    public void HashAndVerify_UsesNonPlaintextHashAndRejectsWrongInput()
    {
        using var fixture = new SecurityTestFixture();
        var user = CreateUser();

        var hash = fixture.PasswordHashService.HashPassword(user, "ValidPass123");
        var hashedUser = user.WithPasswordHash(hash, DateTimeOffset.UtcNow);

        Assert.NotEqual("ValidPass123", hash);
        Assert.Equal(PasswordHashVerificationResult.Success, fixture.PasswordHashService.VerifyPassword(hashedUser, "ValidPass123"));
        Assert.Equal(PasswordHashVerificationResult.Failed, fixture.PasswordHashService.VerifyPassword(hashedUser, "WrongPass123"));
    }

    private static AppUser CreateUser()
        => new(
            Guid.NewGuid(),
            "operator",
            AppUser.NormalizeUsername("operator"),
            "hash",
            UserRole.User,
            isEnabled: true,
            failedLoginCount: 0,
            lockoutUntilUtc: null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            lastLoginAtUtc: null,
            rowVersion: 0);
}
