namespace Configurator.Application.Services.Authorization;

public interface IAccessDecisionService
{
    AccessDecision Authorize(AccessRequirement requirement);

    Task<AccessDecision> AuthorizeAsync(
        AccessRequirement requirement,
        CancellationToken cancellationToken = default);
}
