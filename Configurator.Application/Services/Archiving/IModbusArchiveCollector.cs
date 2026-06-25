namespace Configurator.Application.Services.Archiving;

public interface IModbusArchiveCollector : IAsyncDisposable
{
    Task<ArchiveOperationResult> StartAsync(CancellationToken cancellationToken = default);

    Task<ArchiveOperationResult> StopAsync(CancellationToken cancellationToken = default);
}
