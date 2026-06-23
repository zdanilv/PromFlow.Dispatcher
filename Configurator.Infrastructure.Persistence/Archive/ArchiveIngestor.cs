using Configurator.Application.Services.Archiving;
using Microsoft.Extensions.Options;

namespace Configurator.Infrastructure.Persistence.Archive;

public sealed class ArchiveIngestor : IArchiveIngestor
{
    private readonly ArchivePriorityBuffer _buffer;
    private readonly IOptions<ArchiveOptions> _options;

    public ArchiveIngestor(ArchivePriorityBuffer buffer, IOptions<ArchiveOptions> options)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public ValueTask<ArchiveOperationResult> EnqueueAsync(
        ArchiveEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!_options.Value.Enabled)
        {
            return ValueTask.FromResult(ArchiveOperationResult.Success());
        }

        return _buffer.EnqueueAsync(envelope, cancellationToken);
    }
}
