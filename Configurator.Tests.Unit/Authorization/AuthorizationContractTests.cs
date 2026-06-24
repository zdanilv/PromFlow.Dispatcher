using Configurator.Application.Services.Authorization;
using Xunit;

namespace Configurator.Tests.Unit.Authorization;

public sealed class AuthorizationContractTests
{
    [Fact]
    public void AuthenticationOptionsValidator_DefaultsSucceedAndInvalidPolicyFails()
    {
        var validator = new AuthenticationOptionsValidator();

        var valid = validator.Validate(new AuthenticationOptions());
        var invalid = validator.Validate(new AuthenticationOptions
        {
            MaxFailedAttempts = 0,
            LockoutMinutes = 0,
            MinimumPasswordLength = 4
        });

        Assert.True(valid.Succeeded);
        Assert.Empty(valid.Errors);
        Assert.False(invalid.Succeeded);
        Assert.Contains(invalid.Errors, error => error.PropertyName == nameof(AuthenticationOptions.MaxFailedAttempts));
        Assert.Contains(invalid.Errors, error => error.PropertyName == nameof(AuthenticationOptions.LockoutMinutes));
        Assert.Contains(invalid.Errors, error => error.PropertyName == nameof(AuthenticationOptions.MinimumPasswordLength));
    }

    [Fact]
    public void AppUser_NormalizesUtcTimestampsAndUsername()
    {
        var local = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.FromHours(3));

        var user = new AppUser(
            Guid.NewGuid(),
            " operator ",
            AppUser.NormalizeUsername(" operator "),
            "hash",
            UserRole.User,
            isEnabled: true,
            failedLoginCount: 0,
            lockoutUntilUtc: local.AddMinutes(5),
            local,
            local,
            local,
            lastLoginAtUtc: local,
            rowVersion: 0);

        Assert.Equal("operator", user.Username);
        Assert.Equal("OPERATOR", user.NormalizedUsername);
        Assert.Equal(TimeSpan.Zero, user.CreatedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, user.LockoutUntilUtc!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, user.LastLoginAtUtc!.Value.Offset);
    }

    [Fact]
    public void UserSession_DefensivelyCopiesPermissions()
    {
        var permissions = new List<Permission>
        {
            Permission.ViewRouteMap,
            Permission.IssueEquipmentCommands
        };

        var session = new UserSession(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "operator",
            UserRole.User,
            permissions,
            DateTimeOffset.Now);
        permissions.Clear();

        Assert.True(session.HasPermission(Permission.ViewRouteMap));
        Assert.True(session.HasPermission(Permission.IssueEquipmentCommands));
        Assert.False(session.HasPermission(Permission.ManageUsers));
        Assert.Equal(TimeSpan.Zero, session.CreatedAtUtc.Offset);
    }

    [Fact]
    public void AuthenticationRequest_TrimsUsernameButKeepsSecretInput()
    {
        var request = new AuthenticationRequest(" operator ", "  value  ");

        Assert.Equal("operator", request.Username);
        Assert.Equal("  value  ", request.Password);
    }
}
