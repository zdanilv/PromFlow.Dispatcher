using System.Collections.ObjectModel;

namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Result metadata for a completed archive export.
/// </summary>
public sealed record ArchiveExportResult
{
    public ArchiveExportResult(
        string exportPath,
        DateTimeOffset createdAtUtc,
        long commandCount,
        long physicalWriteCount,
        long runtimeEventCount,
        long snapshotMetadataCount,
        IReadOnlyDictionary<string, string> sha256ByEntryName)
    {
        ArchiveContractGuards.NonNegative(commandCount, nameof(commandCount));
        ArchiveContractGuards.NonNegative(physicalWriteCount, nameof(physicalWriteCount));
        ArchiveContractGuards.NonNegative(runtimeEventCount, nameof(runtimeEventCount));
        ArchiveContractGuards.NonNegative(snapshotMetadataCount, nameof(snapshotMetadataCount));

        ExportPath = ArchiveContractGuards.NotBlank(exportPath, nameof(exportPath));
        CreatedAtUtc = ArchiveContractGuards.Utc(createdAtUtc);
        CommandCount = commandCount;
        PhysicalWriteCount = physicalWriteCount;
        RuntimeEventCount = runtimeEventCount;
        SnapshotMetadataCount = snapshotMetadataCount;
        Sha256ByEntryName = CopyChecksums(sha256ByEntryName);
    }

    public string ExportPath { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public long CommandCount { get; }

    public long PhysicalWriteCount { get; }

    public long RuntimeEventCount { get; }

    public long SnapshotMetadataCount { get; }

    public IReadOnlyDictionary<string, string> Sha256ByEntryName { get; }

    private static IReadOnlyDictionary<string, string> CopyChecksums(
        IReadOnlyDictionary<string, string> checksums)
    {
        ArgumentNullException.ThrowIfNull(checksums);

        return new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(checksums, StringComparer.Ordinal));
    }
}
