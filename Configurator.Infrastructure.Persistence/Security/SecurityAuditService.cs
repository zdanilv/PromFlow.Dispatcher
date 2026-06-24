using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Authorization;

namespace Configurator.Infrastructure.Persistence.Security;

public sealed class SecurityAuditService : ISecurityAuditService
{
    private readonly IArchiveIngestor _ingestor;

    public SecurityAuditService(IArchiveIngestor ingestor)
    {
        _ingestor = ingestor ?? throw new ArgumentNullException(nameof(ingestor));
    }

    public async Task<ArchiveOperationResult> RecordAsync(
        SecurityAuditRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        return await _ingestor.EnqueueAsync(
            new ArchiveEnvelope(
                Guid.NewGuid(),
                ArchiveRecordKind.SecurityAudit,
                ArchivePriority.Critical,
                record,
                DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);
    }
}
