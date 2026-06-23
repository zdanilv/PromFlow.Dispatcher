namespace Configurator.Infrastructure.Persistence.Archive;

public sealed record ArchivePriorityBufferSnapshot(
    int CriticalDepth,
    int NormalDepth,
    bool HasTelemetry,
    int QueueDepth,
    long DroppedTelemetryCount);
