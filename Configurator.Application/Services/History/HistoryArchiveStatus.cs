namespace Configurator.Application.Services.History;

public enum HistoryArchiveState
{
    Starting,
    Running,
    Disabled,
    Archiving,
    Warning,
    Error,
    StorageInUse,
    Stopped
}

public sealed record HistoryArchiveStatus
{
    public HistoryArchiveState State { get; init; } = HistoryArchiveState.Starting;
    public bool StorageReady { get; init; }
    public DateTimeOffset? LastSuccessfulArchiveUtc { get; init; }
    public DateTimeOffset? LastArchiveAttemptUtc { get; init; }
    public string? LastSuccessfulArchivePath { get; init; }
    public long LastArchivedEventId { get; init; }
    public long LastArchiveEventCount { get; init; }
    public long LastArchiveSizeBytes { get; init; }
    public DateTimeOffset? NextArchiveDueUtc { get; init; }
    public long PersistedEventsCount { get; init; }
    public long RejectedEventsCount { get; init; }
    public int PendingQueueCount { get; init; }
    public string? LastErrorCode { get; init; }
    public string? LastErrorMessage { get; init; }
}
