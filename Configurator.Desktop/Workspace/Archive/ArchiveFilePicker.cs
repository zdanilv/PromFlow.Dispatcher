using Avalonia.Platform.Storage;
using Configurator.Desktop.Main;

namespace Configurator.Desktop.Workspace.Archive;

public sealed class ArchiveFilePicker(MainWindow mainWindow) : IArchiveFilePicker
{
    public async Task<string?> PickExportDirectoryAsync(CancellationToken cancellationToken = default)
        => await PickFolderAsync("Select archive export directory", cancellationToken).ConfigureAwait(true);

    public async Task<string?> PickBackupDirectoryAsync(CancellationToken cancellationToken = default)
        => await PickFolderAsync("Select archive backup directory", cancellationToken).ConfigureAwait(true);

    private async Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var folders = await mainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        cancellationToken.ThrowIfCancellationRequested();

        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
}
