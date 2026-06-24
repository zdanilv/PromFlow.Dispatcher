using Configurator.Application.Services.Authorization;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Security;

public sealed class SqliteUserRepositoryTests
{
    [Fact]
    public async Task CreateFindUpdate_EnforcesNormalizedDuplicateAndRowVersion()
    {
        using var fixture = new SecurityTestFixture();
        var user = CreateUser("operator");

        var create = await fixture.UserRepository.CreateAsync(user, CancellationToken.None);
        var duplicate = await fixture.UserRepository.CreateAsync(CreateUser(" OPERATOR "), CancellationToken.None);
        var stored = await fixture.UserRepository.FindByNormalizedUsernameAsync(AppUser.NormalizeUsername("operator"), CancellationToken.None);
        var staleUpdate = await fixture.UserRepository.UpdateAsync(stored! with { IsEnabled = false }, expectedRowVersion: 99, CancellationToken.None);
        var update = await fixture.UserRepository.UpdateAsync(stored! with { IsEnabled = false }, stored.RowVersion, CancellationToken.None);

        Assert.True(create.Succeeded, create.ErrorMessage);
        Assert.False(duplicate.Succeeded);
        Assert.Equal("UserDuplicate", duplicate.ErrorCode);
        Assert.NotNull(stored);
        Assert.False(staleUpdate.Succeeded);
        Assert.Equal("UserRowVersionConflict", staleUpdate.ErrorCode);
        Assert.True(update.Succeeded, update.ErrorMessage);
        Assert.False(update.Value!.IsEnabled);
        Assert.Equal(stored.RowVersion + 1, update.Value.RowVersion);
    }

    [Fact]
    public async Task BootstrapAdministrator_SucceedsOnlyWhenDatabaseHasNoUsers()
    {
        using var fixture = new SecurityTestFixture();
        var admin = CreateUser("admin") with { Role = UserRole.Administrator };

        var first = await fixture.UserRepository.BootstrapAdministratorAsync(admin, CancellationToken.None);
        var second = await fixture.UserRepository.BootstrapAdministratorAsync(CreateUser("other") with { Role = UserRole.Administrator }, CancellationToken.None);

        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.False(second.Succeeded);
        Assert.Equal("BootstrapAlreadyCompleted", second.ErrorCode);
        Assert.Equal(1, await fixture.UserRepository.CountAsync(CancellationToken.None));
    }

    private static AppUser CreateUser(string username)
    {
        var now = DateTimeOffset.UtcNow;
        return new AppUser(
            Guid.NewGuid(),
            username,
            AppUser.NormalizeUsername(username),
            "hash",
            UserRole.User,
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
