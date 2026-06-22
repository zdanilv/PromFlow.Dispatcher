namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Operational archive maintenance boundary.
/// </summary>
public interface IArchiveMaintenanceService
{
    Task<ArchiveOperationResult> ApplyRetentionAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<ArchiveOperationResult> ExportAsync(
        ArchiveQuery query,
        string exportDirectory,
        CancellationToken cancellationToken = default);

    Task<ArchiveOperationResult> CreateBackupAsync(
        string destinationDirectory,
        CancellationToken cancellationToken = default);
}
