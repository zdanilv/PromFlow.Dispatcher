namespace Configurator.Application.Services.Authorization;

public sealed class NoLicenseFeatureGate : ILicenseFeatureGate
{
    public AccessDecision AuthorizeFeature(AccessRequirement requirement, UserSession session)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(session);

        return requirement.RequiredLicenseFeature is null
            ? AccessDecision.Allow(requirement, session)
            : AccessDecision.Deny(requirement, "LicenseFeatureUnavailable", session);
    }
}
