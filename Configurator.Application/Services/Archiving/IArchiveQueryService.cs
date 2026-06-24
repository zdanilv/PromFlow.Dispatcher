namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Bounded, paged archive query boundary.
/// </summary>
public interface IArchiveQueryService
{
    Task<ArchiveOperationResult<ArchivePage<RawModbusSnapshotArchiveMetadataRecord>>> QueryRawSnapshotMetadataAsync(
        ArchiveQuery query,
        CancellationToken cancellationToken = default);

    Task<ArchiveOperationResult<ArchivePage<RawModbusSnapshotArchiveRecord>>> QueryRawSnapshotsAsync(
        ArchiveQuery query,
        CancellationToken cancellationToken = default);

    Task<ArchiveOperationResult<RawModbusSnapshotArchiveRecord>> GetRawSnapshotAsync(
        Guid id,
        DateTimeOffset capturedAtUtc,
        CancellationToken cancellationToken = default);

    Task<ArchiveOperationResult<ArchivePage<ModbusStatusArchiveRecord>>> QueryModbusStatusesAsync(
        ArchiveQuery query,
        CancellationToken cancellationToken = default);

    Task<ArchiveOperationResult<ArchivePage<ArchiveRuntimeEventRecord>>> QueryRuntimeEventsAsync(
        ArchiveQuery query,
        CancellationToken cancellationToken = default);

    Task<ArchiveOperationResult<ArchivePage<EquipmentCommandAuditRecord>>> QueryEquipmentCommandsAsync(
        ArchiveQuery query,
        CancellationToken cancellationToken = default);

    Task<ArchiveOperationResult<ArchivePage<PhysicalModbusWriteAuditRecord>>> QueryPhysicalWritesAsync(
        ArchiveQuery query,
        CancellationToken cancellationToken = default);

    Task<ArchiveOperationResult<ArchivePage<SecurityAuditRecord>>> QuerySecurityAuditAsync(
        ArchiveQuery query,
        CancellationToken cancellationToken = default);
}
