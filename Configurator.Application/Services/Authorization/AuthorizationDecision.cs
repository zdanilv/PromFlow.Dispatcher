namespace Configurator.Application.Services.Authorization;

public sealed record AuthorizationDecision
{
    private AuthorizationDecision(
        bool succeeded,
        Permission permission,
        string? reasonCode,
        Guid? userId,
        string? username)
    {
        Succeeded = succeeded;
        Permission = permission;
        ReasonCode = reasonCode;
        UserId = userId;
        Username = username;
    }

    public bool Succeeded { get; }

    public Permission Permission { get; }

    public string? ReasonCode { get; }

    public Guid? UserId { get; }

    public string? Username { get; }

    public static AuthorizationDecision Allow(Permission permission, UserSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new AuthorizationDecision(true, permission, null, session.UserId, session.Username);
    }

    public static AuthorizationDecision Deny(Permission permission, string reasonCode, UserSession? session = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        return new AuthorizationDecision(false, permission, reasonCode, session?.UserId, session?.Username);
    }
}
