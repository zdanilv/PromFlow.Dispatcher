namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Operational archive maintenance boundary.
/// </summary>
public interface IArchiveMaintenanceService
{
    Task<ArchiveOperationResult<ArchiveRetentionResult>> ApplyRetentionAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<ArchiveOperationResult<ArchiveExportResult>> ExportAsync(
        ArchiveExportRequest request,
        CancellationToken cancellationToken = default);

    Task<ArchiveOperationResult<ArchiveBackupResult>> CreateBackupAsync(
        string destinationDirectory,
        CancellationToken cancellationToken = default);
}
