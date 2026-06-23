namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Retention work performed against one archive partition.
/// </summary>
public sealed record ArchiveRetentionPartitionResult
{
    public ArchiveRetentionPartitionResult(
        string partitionPath,
        long deletedHighResolutionSnapshotRows,
        long deletedLongTermSnapshotRows,
        long deletedRuntimeEventRows,
        long deletedCommandRows,
        long deletedPhysicalWriteRows,
        long deletedSecurityAuditRows,
        bool databaseFileDeleted)
    {
        ArchiveContractGuards.NonNegative(deletedHighResolutionSnapshotRows, nameof(deletedHighResolutionSnapshotRows));
        ArchiveContractGuards.NonNegative(deletedLongTermSnapshotRows, nameof(deletedLongTermSnapshotRows));
        ArchiveContractGuards.NonNegative(deletedRuntimeEventRows, nameof(deletedRuntimeEventRows));
        ArchiveContractGuards.NonNegative(deletedCommandRows, nameof(deletedCommandRows));
        ArchiveContractGuards.NonNegative(deletedPhysicalWriteRows, nameof(deletedPhysicalWriteRows));
        ArchiveContractGuards.NonNegative(deletedSecurityAuditRows, nameof(deletedSecurityAuditRows));

        PartitionPath = ArchiveContractGuards.NotBlank(partitionPath, nameof(partitionPath));
        DeletedHighResolutionSnapshotRows = deletedHighResolutionSnapshotRows;
        DeletedLongTermSnapshotRows = deletedLongTermSnapshotRows;
        DeletedRuntimeEventRows = deletedRuntimeEventRows;
        DeletedCommandRows = deletedCommandRows;
        DeletedPhysicalWriteRows = deletedPhysicalWriteRows;
        DeletedSecurityAuditRows = deletedSecurityAuditRows;
        DatabaseFileDeleted = databaseFileDeleted;
    }

    public string PartitionPath { get; }

    public long DeletedHighResolutionSnapshotRows { get; }

    public long DeletedLongTermSnapshotRows { get; }

    public long DeletedRuntimeEventRows { get; }

    public long DeletedCommandRows { get; }

    public long DeletedPhysicalWriteRows { get; }

    public long DeletedSecurityAuditRows { get; }

    public bool DatabaseFileDeleted { get; }
}
