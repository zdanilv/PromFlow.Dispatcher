namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Application boundary for enqueueing archive records.
/// </summary>
public interface IArchiveIngestor
{
    ValueTask<ArchiveOperationResult> EnqueueAsync(
        ArchiveEnvelope envelope,
        CancellationToken cancellationToken = default);
}
