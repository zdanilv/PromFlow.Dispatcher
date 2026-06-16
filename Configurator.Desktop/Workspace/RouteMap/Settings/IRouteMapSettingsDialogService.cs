namespace Configurator.Desktop.Workspace.RouteMap.Settings;

public interface IRouteMapSettingsDialogService
{
    Task ShowAsync(CancellationToken cancellationToken = default);
}

public interface IRouteMapSettingsFilePicker
{
    Task<string?> PickImportPathAsync(CancellationToken cancellationToken = default);
    Task<string?> PickExportPathAsync(CancellationToken cancellationToken = default);
}
