using Configurator.Application.Services.Licensing;

namespace Configurator.Application.Services.Authorization;

public sealed class LicenseFeatureGate : ILicenseFeatureGate
{
    private readonly ILicenseStateAccessor _licenseStateAccessor;

    public LicenseFeatureGate(ILicenseStateAccessor licenseStateAccessor)
    {
        _licenseStateAccessor = licenseStateAccessor ?? throw new ArgumentNullException(nameof(licenseStateAccessor));
    }

    public AccessDecision AuthorizeFeature(AccessRequirement requirement, UserSession session)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(session);

        if (requirement.RequiredLicenseFeature is null)
        {
            return AccessDecision.Allow(requirement, session);
        }

        var state = _licenseStateAccessor.Current;
        if (!state.IsValid || state.Payload is null)
        {
            return AccessDecision.Deny(requirement, ReasonForState(state), session);
        }

        return state.Payload.Features.Contains(requirement.RequiredLicenseFeature, StringComparer.Ordinal)
            ? AccessDecision.Allow(requirement, session)
            : AccessDecision.Deny(requirement, "LicenseFeatureUnavailable", session);
    }

    private static string ReasonForState(LicenseState state)
        => state.Status switch
        {
            LicenseStatus.Missing => "LicenseMissing",
            LicenseStatus.NotYetValid => "LicenseNotYetValid",
            LicenseStatus.Expired => "LicenseExpired",
            LicenseStatus.ProductMismatch => "LicenseWrongProduct",
            LicenseStatus.ProductVersionMismatch => "LicenseWrongProductVersion",
            LicenseStatus.InstallationMismatch => "LicenseWrongInstallation",
            LicenseStatus.ClockRollbackDetected => "LicenseClockRollback",
            LicenseStatus.StoreUnavailable => "LicenseStorageError",
            _ => "LicenseInvalid",
        };
}
