namespace Configurator.Desktop.Workspace.RouteMap.Configuration;

public interface IRouteMapConfigurationMutationService
{
    RouteMapConfigurationOperationResult Apply(RouteMapConfigurationDocument document);

    Task<RouteMapConfigurationOperationResult> SaveAndApplyAsync(
        RouteMapConfigurationDocument document,
        CancellationToken cancellationToken = default);

    Task<RouteMapConfigurationOperationResult> ExportDraftAsync(
        string path,
        RouteMapConfigurationDocument document,
        CancellationToken cancellationToken = default);
}
