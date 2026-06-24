using Configurator.Application.Services.Archiving;

namespace Configurator.Desktop.Workspace.Archive;

public sealed class ArchiveCommandAuditRowViewModel
{
    public ArchiveCommandAuditRowViewModel(EquipmentCommandAuditRecord record)
    {
        CommandId = record.CommandId;
        RequestedAtUtc = record.RequestedAtUtc.ToString("u");
        CompletedAtUtc = record.CompletedAtUtc?.ToString("u") ?? string.Empty;
        Username = record.Username ?? string.Empty;
        DeviceId = record.DeviceId;
        SignalId = record.SignalId;
        RequestedValue = record.RequestedValueCanonical;
        Outcome = FormatOutcome(record);
        Confirmation = record.ConfirmationStatus.ToString();
        Error = string.IsNullOrWhiteSpace(record.ErrorCode)
            ? record.ErrorMessage ?? string.Empty
            : $"{record.ErrorCode}: {record.ErrorMessage}";
    }

    public Guid CommandId { get; }
    public string RequestedAtUtc { get; }
    public string CompletedAtUtc { get; }
    public string Username { get; }
    public string DeviceId { get; }
    public string SignalId { get; }
    public string RequestedValue { get; }
    public string Outcome { get; }
    public string Confirmation { get; }
    public string Error { get; }

    private static string FormatOutcome(EquipmentCommandAuditRecord record)
    {
        var outcome = record switch { { Result: var value } => value };
        return outcome.ToString();
    }
}
