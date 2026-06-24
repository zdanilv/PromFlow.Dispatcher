using Configurator.Application.Services.Archiving;

namespace Configurator.Application.Services.Authorization;

public interface ISecurityAuditService
{
    Task<ArchiveOperationResult> RecordAsync(
        SecurityAuditRecord record,
        CancellationToken cancellationToken = default);
}
