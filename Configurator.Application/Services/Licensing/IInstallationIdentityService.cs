namespace Configurator.Application.Services.Licensing;

public interface IInstallationIdentityService
{
    Task<InstallationIdentity> GetOrCreateAsync(CancellationToken cancellationToken = default);

    Task<InstallationIdentityRequest> CreateRequestAsync(CancellationToken cancellationToken = default);
}
