namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Coarse health state of the archive subsystem.
/// </summary>
public enum ArchiveHealthState
{
    Stopped,
    Healthy,
    Degraded,
    Faulted
}

/// <summary>
/// Observable archive health snapshot.
/// </summary>
public sealed record ArchiveHealth
{
    public ArchiveHealth(
        ArchiveHealthState state,
        DateTimeOffset updatedAtUtc,
        string message,
        int queueDepth,
        long droppedTelemetryCount,
        DateTimeOffset? lastSuccessAtUtc = null,
        string? lastErrorCode = null,
        string? lastErrorMessage = null)
    {
        ArchiveContractGuards.NonNegative(queueDepth, nameof(queueDepth));
        ArchiveContractGuards.NonNegative(droppedTelemetryCount, nameof(droppedTelemetryCount));

        State = state;
        UpdatedAtUtc = ArchiveContractGuards.Utc(updatedAtUtc);
        Message = message ?? string.Empty;
        QueueDepth = queueDepth;
        DroppedTelemetryCount = droppedTelemetryCount;
        LastSuccessAtUtc = ArchiveContractGuards.Utc(lastSuccessAtUtc);
        LastErrorCode = lastErrorCode;
        LastErrorMessage = lastErrorMessage;
    }

    public ArchiveHealthState State { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    public string Message { get; }

    public int QueueDepth { get; }

    public long DroppedTelemetryCount { get; }

    public DateTimeOffset? LastSuccessAtUtc { get; }

    public string? LastErrorCode { get; }

    public string? LastErrorMessage { get; }

    public static ArchiveHealth Stopped { get; } = new(
        ArchiveHealthState.Stopped,
        DateTimeOffset.UnixEpoch,
        "Archive stopped.",
        queueDepth: 0,
        droppedTelemetryCount: 0);
}
