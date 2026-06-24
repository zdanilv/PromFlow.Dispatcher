namespace Configurator.Application.Services.Authorization;

public interface ILicenseFeatureGate
{
    AccessDecision AuthorizeFeature(AccessRequirement requirement, UserSession session);
}
