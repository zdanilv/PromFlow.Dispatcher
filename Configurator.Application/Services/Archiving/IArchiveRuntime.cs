namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Deterministic lifecycle contract for the archive runtime.
/// </summary>
public interface IArchiveRuntime : IAsyncDisposable
{
    Task<ArchiveOperationResult> StartAsync(CancellationToken cancellationToken = default);

    Task<ArchiveOperationResult> FlushAsync(CancellationToken cancellationToken = default);

    Task<ArchiveOperationResult> StopAsync(CancellationToken cancellationToken = default);
}
