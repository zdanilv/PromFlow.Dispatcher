using Configurator.Application.Services.Authorization;

namespace Configurator.Application.Services.Archiving;

public sealed class AuthorizedArchiveMaintenanceService : IArchiveMaintenanceService
{
    private readonly IArchiveMaintenanceService _inner;
    private readonly IAccessDecisionService _accessDecisionService;

    public AuthorizedArchiveMaintenanceService(
        IArchiveMaintenanceService inner,
        IAccessDecisionService accessDecisionService)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _accessDecisionService = accessDecisionService ?? throw new ArgumentNullException(nameof(accessDecisionService));
    }

    public async Task<ArchiveOperationResult<ArchiveRetentionResult>> ApplyRetentionAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var decision = await AuthorizeAsync(Permission.RunArchiveMaintenance, cancellationToken)
            .ConfigureAwait(false);
        return decision.Succeeded
            ? await _inner.ApplyRetentionAsync(nowUtc, cancellationToken).ConfigureAwait(false)
            : ArchiveOperationResult<ArchiveRetentionResult>.Failure(
                "PermissionDenied",
                "Archive maintenance permission is required.",
                decision.ReasonCode);
    }

    public async Task<ArchiveOperationResult<ArchiveExportResult>> ExportAsync(
        ArchiveExportRequest request,
        CancellationToken cancellationToken = default)
    {
        var decision = await AuthorizeAsync(Permission.ExportArchive, cancellationToken)
            .ConfigureAwait(false);
        return decision.Succeeded
            ? await _inner.ExportAsync(request, cancellationToken).ConfigureAwait(false)
            : ArchiveOperationResult<ArchiveExportResult>.Failure(
                "PermissionDenied",
                "Archive export permission is required.",
                decision.ReasonCode);
    }

    public async Task<ArchiveOperationResult<ArchiveBackupResult>> CreateBackupAsync(
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        var decision = await AuthorizeAsync(Permission.RunArchiveMaintenance, cancellationToken)
            .ConfigureAwait(false);
        return decision.Succeeded
            ? await _inner.CreateBackupAsync(destinationDirectory, cancellationToken).ConfigureAwait(false)
            : ArchiveOperationResult<ArchiveBackupResult>.Failure(
                "PermissionDenied",
                "Archive maintenance permission is required.",
                decision.ReasonCode);
    }

    private Task<AccessDecision> AuthorizeAsync(
        Permission permission,
        CancellationToken cancellationToken)
        => _accessDecisionService.AuthorizeAsync(new AccessRequirement(permission), cancellationToken);
}
