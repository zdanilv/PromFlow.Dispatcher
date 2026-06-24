using Configurator.Application.Services.Archiving;

namespace Configurator.Desktop.Workspace.Archive;

public sealed class ArchivePhysicalWriteRowViewModel
{
    public ArchivePhysicalWriteRowViewModel(PhysicalModbusWriteAuditRecord record)
    {
        WriteId = record.WriteId;
        CommandId = record.CommandId?.ToString("D") ?? string.Empty;
        AttemptedAtUtc = record.AttemptedAtUtc.ToString("u");
        CompletedAtUtc = record.CompletedAtUtc?.ToString("u") ?? string.Empty;
        Role = record.Role.ToString();
        Area = record.Area.ToString();
        Address = record.Address;
        Quantity = record.Quantity;
        PayloadHex = Convert.ToHexString(record.PayloadBlob.ToArray());
        Succeeded = record.Succeeded;
        Error = string.IsNullOrWhiteSpace(record.ErrorCode)
            ? record.ErrorMessage ?? string.Empty
            : $"{record.ErrorCode}: {record.ErrorMessage}";
    }

    public Guid WriteId { get; }
    public string CommandId { get; }
    public string AttemptedAtUtc { get; }
    public string CompletedAtUtc { get; }
    public string Role { get; }
    public string Area { get; }
    public int Address { get; }
    public int Quantity { get; }
    public string PayloadHex { get; }
    public bool Succeeded { get; }
    public string Error { get; }
}
