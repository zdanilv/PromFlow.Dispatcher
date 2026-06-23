namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Runtime archive event row projected from archive storage.
/// </summary>
public sealed record ArchiveRuntimeEventRecord
{
    public ArchiveRuntimeEventRecord(
        Guid id,
        DateTimeOffset occurredAtUtc,
        string? deviceId,
        string eventType,
        int severity,
        string message,
        string? detailsJson)
    {
        ArchiveContractGuards.NotEmpty(id, nameof(id));
        ArchiveContractGuards.NonNegative(severity, nameof(severity));

        Id = id;
        OccurredAtUtc = ArchiveContractGuards.Utc(occurredAtUtc);
        DeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim();
        EventType = ArchiveContractGuards.NotBlank(eventType, nameof(eventType));
        Severity = severity;
        Message = ArchiveContractGuards.NotBlank(message, nameof(message));
        DetailsJson = detailsJson;
    }

    public Guid Id { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    public string? DeviceId { get; }

    public string EventType { get; }

    public int Severity { get; }

    public string Message { get; }

    public string? DetailsJson { get; }
}
