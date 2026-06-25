namespace Configurator.Application.Services.Runtime;

public interface IApplicationRuntimeCoordinator
{
    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
