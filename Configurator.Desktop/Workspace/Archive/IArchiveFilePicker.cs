namespace Configurator.Desktop.Workspace.Archive;

public interface IArchiveFilePicker
{
    Task<string?> PickExportDirectoryAsync(CancellationToken cancellationToken = default);

    Task<string?> PickBackupDirectoryAsync(CancellationToken cancellationToken = default);
}
