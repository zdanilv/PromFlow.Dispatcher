namespace Configurator.Application.Services.Authorization;

public sealed class DefaultAccessDecisionService : IAccessDecisionService
{
    private readonly IUserSessionAccessor _sessionAccessor;
    private readonly ILicenseFeatureGate _licenseFeatureGate;

    public DefaultAccessDecisionService(
        IUserSessionAccessor sessionAccessor,
        ILicenseFeatureGate licenseFeatureGate)
    {
        _sessionAccessor = sessionAccessor ?? throw new ArgumentNullException(nameof(sessionAccessor));
        _licenseFeatureGate = licenseFeatureGate ?? throw new ArgumentNullException(nameof(licenseFeatureGate));
    }

    public AccessDecision Authorize(AccessRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        if (!Enum.IsDefined(requirement.RequiredPermission))
        {
            return AccessDecision.Deny(requirement, "PermissionUnsupported");
        }

        var snapshot = _sessionAccessor.Current;
        if (!snapshot.IsAuthenticated || snapshot.Session is null)
        {
            return AccessDecision.Deny(requirement, "NotAuthenticated");
        }

        var session = snapshot.Session;
        if (!session.HasPermission(requirement.RequiredPermission))
        {
            return AccessDecision.Deny(requirement, "PermissionDenied", session);
        }

        return _licenseFeatureGate.AuthorizeFeature(requirement, session);
    }

    public Task<AccessDecision> AuthorizeAsync(
        AccessRequirement requirement,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Authorize(requirement));
    }
}
