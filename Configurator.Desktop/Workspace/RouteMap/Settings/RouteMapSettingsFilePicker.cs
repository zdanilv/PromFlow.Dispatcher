using Avalonia.Platform.Storage;
using Configurator.Desktop.Main;

namespace Configurator.Desktop.Workspace.RouteMap.Settings;

public sealed class RouteMapSettingsFilePicker(MainWindow mainWindow) : IRouteMapSettingsFilePicker
{
    private static readonly FilePickerFileType JsonFileType = new("JSON")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"],
    };

    public async Task<string?> PickImportPathAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = await mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Импорт настроек RouteMap",
            AllowMultiple = false,
            FileTypeFilter = [JsonFileType],
        });
        cancellationToken.ThrowIfCancellationRequested();
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> PickExportPathAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = await mainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Экспорт настроек RouteMap",
            SuggestedFileName = "route-map.json",
            DefaultExtension = "json",
            FileTypeChoices = [JsonFileType],
        });
        cancellationToken.ThrowIfCancellationRequested();
        return file?.TryGetLocalPath();
    }
}
