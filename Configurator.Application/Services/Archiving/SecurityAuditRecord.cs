namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Severity of a security audit event.
/// </summary>
public enum SecurityAuditSeverity
{
    Information,
    Warning,
    Error
}

/// <summary>
/// Result of a security-sensitive action.
/// </summary>
public enum SecurityAuditResult
{
    Succeeded,
    Denied,
    Failed
}

/// <summary>
/// Sanitized security audit event. Details must not contain full PII payloads or sensitive material.
/// </summary>
public sealed record SecurityAuditRecord
{
    public SecurityAuditRecord(
        Guid id,
        DateTimeOffset occurredAtUtc,
        string eventType,
        SecurityAuditSeverity severity,
        string? actorUserId,
        string? actorUsername,
        string? sessionId,
        string? targetUserId,
        SecurityAuditResult result,
        string? reasonCode,
        string? detailsJson,
        int schemaVersion)
    {
        ArchiveContractGuards.NotEmpty(id, nameof(id));
        ArchiveContractGuards.EnumDefined(severity, nameof(severity));
        ArchiveContractGuards.EnumDefined(result, nameof(result));
        ArchiveContractGuards.Positive(schemaVersion, nameof(schemaVersion));

        Id = id;
        OccurredAtUtc = ArchiveContractGuards.Utc(occurredAtUtc);
        EventType = ArchiveContractGuards.NotBlank(eventType, nameof(eventType));
        Severity = severity;
        ActorUserId = actorUserId;
        ActorUsername = actorUsername;
        SessionId = sessionId;
        TargetUserId = targetUserId;
        Result = result;
        ReasonCode = reasonCode;
        DetailsJson = detailsJson;
        SchemaVersion = schemaVersion;
    }

    public Guid Id { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    public string EventType { get; }

    public SecurityAuditSeverity Severity { get; }

    public string? ActorUserId { get; }

    public string? ActorUsername { get; }

    public string? SessionId { get; }

    public string? TargetUserId { get; }

    public SecurityAuditResult Result { get; }

    public string? ReasonCode { get; }

    public string? DetailsJson { get; }

    public int SchemaVersion { get; }
}
