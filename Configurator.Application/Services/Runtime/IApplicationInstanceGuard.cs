namespace Configurator.Application.Services.Runtime;

public interface IApplicationInstanceGuard
{
    Task<IApplicationInstanceLease> AcquireAsync(CancellationToken cancellationToken = default);
}
