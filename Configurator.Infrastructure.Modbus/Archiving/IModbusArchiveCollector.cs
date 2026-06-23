using Configurator.Application.Services.Archiving;

namespace Configurator.Infrastructure.Modbus.Archiving;

public interface IModbusArchiveCollector : IAsyncDisposable
{
    Task<ArchiveOperationResult> StartAsync(CancellationToken cancellationToken = default);

    Task<ArchiveOperationResult> StopAsync(CancellationToken cancellationToken = default);
}
