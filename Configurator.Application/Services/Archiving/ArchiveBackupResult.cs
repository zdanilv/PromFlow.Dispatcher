using System.Collections.ObjectModel;

namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Result metadata for a completed archive backup package.
/// </summary>
public sealed record ArchiveBackupResult
{
    public ArchiveBackupResult(
        string backupPath,
        DateTimeOffset createdAtUtc,
        IReadOnlyList<ArchiveBackupPartitionResult> partitions,
        IReadOnlyDictionary<string, string> sha256ByEntryName)
    {
        BackupPath = ArchiveContractGuards.NotBlank(backupPath, nameof(backupPath));
        CreatedAtUtc = ArchiveContractGuards.Utc(createdAtUtc);
        Partitions = ArchiveContractGuards.ReadOnlyCopy(partitions, nameof(partitions));
        Sha256ByEntryName = CopyChecksums(sha256ByEntryName);
    }

    public string BackupPath { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public IReadOnlyList<ArchiveBackupPartitionResult> Partitions { get; }

    public IReadOnlyDictionary<string, string> Sha256ByEntryName { get; }

    private static IReadOnlyDictionary<string, string> CopyChecksums(
        IReadOnlyDictionary<string, string> checksums)
    {
        ArgumentNullException.ThrowIfNull(checksums);

        return new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(checksums, StringComparer.Ordinal));
    }
}
