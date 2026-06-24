using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Licensing;

namespace Configurator.Application.Services.Archiving;

public sealed class AuthorizedArchiveQueryService : IArchiveQueryService
{
    private readonly IArchiveQueryService _inner;
    private readonly IAccessDecisionService _accessDecisionService;

    public AuthorizedArchiveQueryService(
        IArchiveQueryService inner,
        IAccessDecisionService accessDecisionService)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _accessDecisionService = accessDecisionService ?? throw new ArgumentNullException(nameof(accessDecisionService));
    }

    public async Task<ArchiveOperationResult<ArchivePage<RawModbusSnapshotArchiveMetadataRecord>>> QueryRawSnapshotMetadataAsync(
        ArchiveQuery query,
        CancellationToken cancellationToken = default)
    {
        var decision = await AuthorizeArchiveAsync(cancellationToken).ConfigureAwait(false);
        return decision.Succeeded
            ? await _inner.QueryRawSnapshotMetadataAsync(query, cancellationToken).ConfigureAwait(false)
            : DenyPage<RawModbusSnapshotArchiveMetadataRecord>(decision, "Archive view permission is required.");
    }

    public async Task<ArchiveOperationResult<ArchivePage<RawModbusSnapshotArchiveRecord>>> QueryRawSnapshotsAsync(
        ArchiveQuery query,
        CancellationToken cancellationToken = default)
    {
        var decision = await AuthorizeArchiveAsync(cancellationToken).ConfigureAwait(false);
        return decision.Succeeded
            ? await _inner.QueryRawSnapshotsAsync(query, cancellationToken).ConfigureAwait(false)
            : DenyPage<RawModbusSnapshotArchiveRecord>(decision, "Archive view permission is required.");
    }

    public async Task<ArchiveOperationResult<RawModbusSnapshotArchiveRecord>> GetRawSnapshotAsync(
        Guid id,
        DateTimeOffset capturedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var decision = await AuthorizeArchiveAsync(cancellationToken).ConfigureAwait(false);
        return decision.Succeeded
            ? await _inner.GetRawSnapshotAsync(id, capturedAtUtc, cancellationToken).ConfigureAwait(false)
            : ArchiveOperationResult<RawModbusSnapshotArchiveRecord>.Failure(
                "PermissionDenied",
                "Archive view permission is required.",
                decision.ReasonCode);
    }

    public async Task<ArchiveOperationResult<ArchivePage<ModbusStatusArchiveRecord>>> QueryModbusStatusesAsync(
        ArchiveQuery query,
        CancellationToken cancellationToken = default)
    {
        var decision = await AuthorizeArchiveAsync(cancellationToken).ConfigureAwait(false);
        return decision.Succeeded
            ? await _inner.QueryModbusStatusesAsync(query, cancellationToken).ConfigureAwait(false)
            : DenyPage<ModbusStatusArchiveRecord>(decision, "Archive view permission is required.");
    }

    public async Task<ArchiveOperationResult<ArchivePage<ArchiveRuntimeEventRecord>>> QueryRuntimeEventsAsync(
        ArchiveQuery query,
        CancellationToken cancellationToken = default)
    {
        var decision = await AuthorizeArchiveAsync(cancellationToken).ConfigureAwait(false);
        return decision.Succeeded
            ? await _inner.QueryRuntimeEventsAsync(query, cancellationToken).ConfigureAwait(false)
            : DenyPage<ArchiveRuntimeEventRecord>(decision, "Archive view permission is required.");
    }

    public async Task<ArchiveOperationResult<ArchivePage<EquipmentCommandAuditRecord>>> QueryEquipmentCommandsAsync(
        ArchiveQuery query,
        CancellationToken cancellationToken = default)
    {
        var decision = await AuthorizeArchiveAsync(cancellationToken).ConfigureAwait(false);
        return decision.Succeeded
            ? await _inner.QueryEquipmentCommandsAsync(query, cancellationToken).ConfigureAwait(false)
            : DenyPage<EquipmentCommandAuditRecord>(decision, "Archive view permission is required.");
    }

    public async Task<ArchiveOperationResult<ArchivePage<PhysicalModbusWriteAuditRecord>>> QueryPhysicalWritesAsync(
        ArchiveQuery query,
        CancellationToken cancellationToken = default)
    {
        var decision = await AuthorizeArchiveAsync(cancellationToken).ConfigureAwait(false);
        return decision.Succeeded
            ? await _inner.QueryPhysicalWritesAsync(query, cancellationToken).ConfigureAwait(false)
            : DenyPage<PhysicalModbusWriteAuditRecord>(decision, "Archive view permission is required.");
    }

    public async Task<ArchiveOperationResult<ArchivePage<SecurityAuditRecord>>> QuerySecurityAuditAsync(
        ArchiveQuery query,
        CancellationToken cancellationToken = default)
    {
        var decision = await _accessDecisionService
            .AuthorizeAsync(new AccessRequirement(Permission.ViewSecurityAudit, LicenseFeature.Archive), cancellationToken)
            .ConfigureAwait(false);
        return decision.Succeeded
            ? await _inner.QuerySecurityAuditAsync(query, cancellationToken).ConfigureAwait(false)
            : DenyPage<SecurityAuditRecord>(decision, "Security audit view permission is required.");
    }

    private Task<AccessDecision> AuthorizeArchiveAsync(CancellationToken cancellationToken)
        => _accessDecisionService.AuthorizeAsync(
            new AccessRequirement(Permission.ViewArchive, LicenseFeature.Archive),
            cancellationToken);

    private static ArchiveOperationResult<ArchivePage<T>> DenyPage<T>(
        AccessDecision decision,
        string message)
        => ArchiveOperationResult<ArchivePage<T>>.Failure(
            "PermissionDenied",
            message,
            decision.ReasonCode);
}
