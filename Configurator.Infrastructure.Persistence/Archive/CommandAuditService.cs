using Configurator.Application.Services.Archiving;

namespace Configurator.Infrastructure.Persistence.Archive;

public sealed class CommandAuditService(IArchiveIngestor ingestor) : ICommandAuditService
{
    public Task<ArchiveOperationResult> RecordCommandAsync(
        EquipmentCommandAuditRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        return EnqueueAsync(
            new ArchiveEnvelope(
                Guid.NewGuid(),
                ArchiveRecordKind.EquipmentCommandAudit,
                ArchivePriority.Critical,
                record,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    public Task<ArchiveOperationResult> RecordPhysicalWriteAsync(
        PhysicalModbusWriteAuditRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        return EnqueueAsync(
            new ArchiveEnvelope(
                Guid.NewGuid(),
                ArchiveRecordKind.PhysicalModbusWriteAudit,
                ArchivePriority.Critical,
                record,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private async Task<ArchiveOperationResult> EnqueueAsync(
        ArchiveEnvelope envelope,
        CancellationToken cancellationToken)
        => await ingestor.EnqueueAsync(envelope, cancellationToken).ConfigureAwait(false);
}
