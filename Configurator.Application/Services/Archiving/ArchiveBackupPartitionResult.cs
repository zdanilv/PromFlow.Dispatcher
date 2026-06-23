namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Backup metadata for one archived SQLite partition.
/// </summary>
public sealed record ArchiveBackupPartitionResult
{
    public ArchiveBackupPartitionResult(
        string sourcePath,
        string entryName,
        long lengthBytes,
        string sha256,
        bool quickCheckPassed)
    {
        ArchiveContractGuards.NonNegative(lengthBytes, nameof(lengthBytes));

        SourcePath = ArchiveContractGuards.NotBlank(sourcePath, nameof(sourcePath));
        EntryName = ArchiveContractGuards.NotBlank(entryName, nameof(entryName));
        LengthBytes = lengthBytes;
        Sha256 = ArchiveContractGuards.NotBlank(sha256, nameof(sha256));
        QuickCheckPassed = quickCheckPassed;
    }

    public string SourcePath { get; }

    public string EntryName { get; }

    public long LengthBytes { get; }

    public string Sha256 { get; }

    public bool QuickCheckPassed { get; }
}
