namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Summary of one archive retention run.
/// </summary>
public sealed record ArchiveRetentionResult
{
    public ArchiveRetentionResult(
        DateTimeOffset appliedAtUtc,
        IReadOnlyList<ArchiveRetentionPartitionResult> partitions)
    {
        AppliedAtUtc = ArchiveContractGuards.Utc(appliedAtUtc);
        Partitions = ArchiveContractGuards.ReadOnlyCopy(partitions, nameof(partitions));
    }

    public DateTimeOffset AppliedAtUtc { get; }

    public IReadOnlyList<ArchiveRetentionPartitionResult> Partitions { get; }

    public long DeletedHighResolutionSnapshotRows
        => Partitions.Sum(partition => partition.DeletedHighResolutionSnapshotRows);

    public long DeletedLongTermSnapshotRows
        => Partitions.Sum(partition => partition.DeletedLongTermSnapshotRows);

    public long DeletedRuntimeEventRows
        => Partitions.Sum(partition => partition.DeletedRuntimeEventRows);

    public long DeletedCommandRows
        => Partitions.Sum(partition => partition.DeletedCommandRows);

    public long DeletedPhysicalWriteRows
        => Partitions.Sum(partition => partition.DeletedPhysicalWriteRows);

    public long DeletedSecurityAuditRows
        => Partitions.Sum(partition => partition.DeletedSecurityAuditRows);

    public long DeletedDatabaseFiles
        => Partitions.Count(partition => partition.DatabaseFileDeleted);
}
