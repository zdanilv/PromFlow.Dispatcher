using Avalonia.Platform.Storage;
using Configurator.Desktop.Main;

namespace Configurator.Desktop.Workspace.Licensing;

public sealed class LicenseFilePicker(MainWindow mainWindow) : ILicenseFilePicker
{
    private static readonly FilePickerFileType LicenseFileType = new("PromFlow license")
    {
        Patterns = ["*.promlicense"],
    };

    private static readonly FilePickerFileType RequestFileType = new("PromFlow license request")
    {
        Patterns = ["*.promrequest"],
    };

    public async Task<string?> PickLicensePathAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = await mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Install license",
            AllowMultiple = false,
            FileTypeFilter = [LicenseFileType],
        });
        cancellationToken.ThrowIfCancellationRequested();

        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> PickRequestExportPathAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = await mainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export installation request",
            SuggestedFileName = "installation.promrequest",
            DefaultExtension = "promrequest",
            FileTypeChoices = [RequestFileType],
        });
        cancellationToken.ThrowIfCancellationRequested();

        return file?.TryGetLocalPath();
    }
}
