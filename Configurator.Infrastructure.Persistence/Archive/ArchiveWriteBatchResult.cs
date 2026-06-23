namespace Configurator.Infrastructure.Persistence.Archive;

public sealed record ArchiveWriteBatchResult
{
    public ArchiveWriteBatchResult(
        int attemptedCount,
        int persistedCount,
        IReadOnlyList<string> partitionPaths,
        DateTimeOffset writtenAtUtc)
    {
        if (attemptedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptedCount), attemptedCount, "Attempted count must not be negative.");
        }

        if (persistedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(persistedCount), persistedCount, "Persisted count must not be negative.");
        }

        ArgumentNullException.ThrowIfNull(partitionPaths);

        AttemptedCount = attemptedCount;
        PersistedCount = persistedCount;
        PartitionPaths = Array.AsReadOnly(partitionPaths.ToArray());
        WrittenAtUtc = writtenAtUtc.ToUniversalTime();
    }

    public int AttemptedCount { get; }

    public int PersistedCount { get; }

    public IReadOnlyList<string> PartitionPaths { get; }

    public DateTimeOffset WrittenAtUtc { get; }
}
