namespace Configurator.Application.Services.Authorization;

public sealed record AccessDecision
{
    private AccessDecision(
        bool succeeded,
        Permission permission,
        string? requiredLicenseFeature,
        string? reasonCode,
        Guid? userId,
        string? username)
    {
        Succeeded = succeeded;
        Permission = permission;
        RequiredLicenseFeature = requiredLicenseFeature;
        ReasonCode = reasonCode;
        UserId = userId;
        Username = username;
    }

    public bool Succeeded { get; }

    public Permission Permission { get; }

    public string? RequiredLicenseFeature { get; }

    public string? ReasonCode { get; }

    public Guid? UserId { get; }

    public string? Username { get; }

    public static AccessDecision Allow(AccessRequirement requirement, UserSession session)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(session);

        return new AccessDecision(
            true,
            requirement.RequiredPermission,
            requirement.RequiredLicenseFeature,
            null,
            session.UserId,
            session.Username);
    }

    public static AccessDecision Deny(
        AccessRequirement requirement,
        string reasonCode,
        UserSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        return new AccessDecision(
            false,
            requirement.RequiredPermission,
            requirement.RequiredLicenseFeature,
            reasonCode,
            session?.UserId,
            session?.Username);
    }
}
