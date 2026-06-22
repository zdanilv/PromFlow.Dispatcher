namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Command and physical write audit boundary.
/// </summary>
public interface ICommandAuditService
{
    Task<ArchiveOperationResult> RecordCommandAsync(
        EquipmentCommandAuditRecord record,
        CancellationToken cancellationToken = default);

    Task<ArchiveOperationResult> RecordPhysicalWriteAsync(
        PhysicalModbusWriteAuditRecord record,
        CancellationToken cancellationToken = default);
}
