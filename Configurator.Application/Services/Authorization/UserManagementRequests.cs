namespace Configurator.Application.Services.Authorization;

public sealed record BootstrapAdministratorRequest(string Username, string Password);

public sealed record CreateUserRequest(string Username, string Password, UserRole Role, bool IsEnabled = true);

public sealed record ChangePasswordRequest(Guid UserId, string NewPassword, long ExpectedRowVersion);

public sealed record SetUserEnabledRequest(Guid UserId, bool IsEnabled, long ExpectedRowVersion);

public sealed record UserSummary(
    Guid Id,
    string Username,
    UserRole Role,
    bool IsEnabled,
    int FailedLoginCount,
    DateTimeOffset? LockoutUntilUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset PasswordChangedAtUtc,
    DateTimeOffset? LastLoginAtUtc,
    long RowVersion);

public sealed record UserListResult(
    bool Succeeded,
    IReadOnlyList<UserSummary> Users,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static UserListResult Success(IReadOnlyList<UserSummary> users)
    {
        ArgumentNullException.ThrowIfNull(users);

        return new UserListResult(true, users, null, null);
    }

    public static UserListResult Failure(string errorCode, string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        return new UserListResult(false, [], errorCode, errorMessage);
    }
}

public sealed record UserManagementResult(
    bool Succeeded,
    AppUser? User,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static UserManagementResult Success(AppUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return new UserManagementResult(true, user, null, null);
    }

    public static UserManagementResult Failure(string errorCode, string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        return new UserManagementResult(false, null, errorCode, errorMessage);
    }
}
