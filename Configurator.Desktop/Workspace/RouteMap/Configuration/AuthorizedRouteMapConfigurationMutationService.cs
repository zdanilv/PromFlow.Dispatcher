using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Licensing;

namespace Configurator.Desktop.Workspace.RouteMap.Configuration;

public sealed class AuthorizedRouteMapConfigurationMutationService : IRouteMapConfigurationMutationService
{
    private readonly RouteMapConfigurationManager _manager;
    private readonly IAccessDecisionService _accessDecisionService;

    public AuthorizedRouteMapConfigurationMutationService(
        RouteMapConfigurationManager manager,
        IAccessDecisionService accessDecisionService)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _accessDecisionService = accessDecisionService ?? throw new ArgumentNullException(nameof(accessDecisionService));
    }

    public RouteMapConfigurationOperationResult Apply(RouteMapConfigurationDocument document)
    {
        var decision = Authorize();
        return decision.Succeeded
            ? _manager.Apply(document)
            : Denied(decision);
    }

    public async Task<RouteMapConfigurationOperationResult> SaveAndApplyAsync(
        RouteMapConfigurationDocument document,
        CancellationToken cancellationToken = default)
    {
        var decision = await AuthorizeAsync(cancellationToken).ConfigureAwait(false);
        return decision.Succeeded
            ? await _manager.SaveAndApplyAsync(document, cancellationToken).ConfigureAwait(false)
            : Denied(decision);
    }

    public async Task<RouteMapConfigurationOperationResult> ExportDraftAsync(
        string path,
        RouteMapConfigurationDocument document,
        CancellationToken cancellationToken = default)
    {
        var decision = await AuthorizeAsync(cancellationToken).ConfigureAwait(false);
        return decision.Succeeded
            ? await _manager.ExportDraftAsync(path, document, cancellationToken).ConfigureAwait(false)
            : Denied(decision);
    }

    private AccessDecision Authorize()
        => _accessDecisionService.Authorize(
            new AccessRequirement(Permission.EditSignalMapping, LicenseFeature.EngineeringTools));

    private Task<AccessDecision> AuthorizeAsync(CancellationToken cancellationToken)
        => _accessDecisionService.AuthorizeAsync(
            new AccessRequirement(Permission.EditSignalMapping, LicenseFeature.EngineeringTools),
            cancellationToken);

    private static RouteMapConfigurationOperationResult Denied(AccessDecision decision)
        => new(false, [], $"PermissionDenied: Edit signal mapping permission is required. Reason: {decision.ReasonCode}");
}
