using Configurator.Application.Services.Archiving;

namespace Configurator.Desktop.Workspace.Archive;

public sealed class ArchiveSecurityAuditRowViewModel
{
    public ArchiveSecurityAuditRowViewModel(SecurityAuditRecord record)
    {
        Id = record.Id;
        OccurredAtUtc = record.OccurredAtUtc.ToString("u");
        EventType = record.EventType;
        Severity = record.Severity.ToString();
        Actor = record.ActorUsername ?? record.ActorUserId ?? string.Empty;
        TargetUserId = record.TargetUserId ?? string.Empty;
        Outcome = FormatOutcome(record);
        ReasonCode = record.ReasonCode ?? string.Empty;
        DetailsJson = record.DetailsJson ?? string.Empty;
    }

    public Guid Id { get; }
    public string OccurredAtUtc { get; }
    public string EventType { get; }
    public string Severity { get; }
    public string Actor { get; }
    public string TargetUserId { get; }
    public string Outcome { get; }
    public string ReasonCode { get; }
    public string DetailsJson { get; }

    private static string FormatOutcome(SecurityAuditRecord record)
    {
        var outcome = record switch { { Result: var value } => value };
        return outcome.ToString();
    }
}
