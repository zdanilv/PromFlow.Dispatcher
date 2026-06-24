namespace Configurator.Application.Services.Authorization;

public sealed record UserSession
{
    public UserSession(
        Guid sessionId,
        Guid userId,
        string username,
        UserRole role,
        IEnumerable<Permission> permissions,
        DateTimeOffset createdAtUtc)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session id must not be empty.", nameof(sessionId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id must not be empty.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(permissions);
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "User role is unsupported.");
        }

        SessionId = sessionId;
        UserId = userId;
        Username = username.Trim();
        Role = role;
        Permissions = Array.AsReadOnly(permissions.Distinct().OrderBy(permission => permission).ToArray());
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public Guid SessionId { get; }

    public Guid UserId { get; }

    public string Username { get; }

    public UserRole Role { get; }

    public IReadOnlyList<Permission> Permissions { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public bool HasPermission(Permission permission)
        => Permissions.Contains(permission);
}
