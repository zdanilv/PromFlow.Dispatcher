namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Default command audit implementation used until persistence is registered.
/// </summary>
public sealed class NoopCommandAuditService : ICommandAuditService
{
    public Task<ArchiveOperationResult> RecordCommandAsync(
        EquipmentCommandAuditRecord record,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ArchiveOperationResult.Success());

    public Task<ArchiveOperationResult> RecordPhysicalWriteAsync(
        PhysicalModbusWriteAuditRecord record,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ArchiveOperationResult.Success());
}
