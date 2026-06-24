namespace Configurator.Application.Services.Authorization;

public sealed record AppUser
{
    public AppUser(
        Guid id,
        string username,
        string normalizedUsername,
        string passwordHash,
        UserRole role,
        bool isEnabled,
        int failedLoginCount,
        DateTimeOffset? lockoutUntilUtc,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset passwordChangedAtUtc,
        DateTimeOffset? lastLoginAtUtc,
        long rowVersion)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("User id must not be empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedUsername);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "User role is unsupported.");
        }

        if (failedLoginCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(failedLoginCount), failedLoginCount, "Failed login count must not be negative.");
        }

        if (rowVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowVersion), rowVersion, "Row version must not be negative.");
        }

        Id = id;
        Username = username.Trim();
        NormalizedUsername = normalizedUsername.Trim();
        PasswordHash = passwordHash;
        Role = role;
        IsEnabled = isEnabled;
        FailedLoginCount = failedLoginCount;
        LockoutUntilUtc = ToUtc(lockoutUntilUtc);
        CreatedAtUtc = ToUtc(createdAtUtc);
        UpdatedAtUtc = ToUtc(updatedAtUtc);
        PasswordChangedAtUtc = ToUtc(passwordChangedAtUtc);
        LastLoginAtUtc = ToUtc(lastLoginAtUtc);
        RowVersion = rowVersion;
    }

    public Guid Id { get; init; }

    public string Username { get; init; }

    public string NormalizedUsername { get; init; }

    public string PasswordHash { get; init; }

    public UserRole Role { get; init; }

    public bool IsEnabled { get; init; }

    public int FailedLoginCount { get; init; }

    public DateTimeOffset? LockoutUntilUtc { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public DateTimeOffset PasswordChangedAtUtc { get; init; }

    public DateTimeOffset? LastLoginAtUtc { get; init; }

    public long RowVersion { get; init; }

    public AppUser WithPasswordHash(string passwordHash, DateTimeOffset changedAtUtc)
        => this with
        {
            PasswordHash = passwordHash,
            PasswordChangedAtUtc = ToUtc(changedAtUtc),
            UpdatedAtUtc = ToUtc(changedAtUtc)
        };

    public static string NormalizeUsername(string username)
        => (username ?? string.Empty).Trim().ToUpperInvariant();

    private static DateTimeOffset ToUtc(DateTimeOffset value)
        => value.ToUniversalTime();

    private static DateTimeOffset? ToUtc(DateTimeOffset? value)
        => value?.ToUniversalTime();
}
