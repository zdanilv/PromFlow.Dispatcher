namespace Configurator.Application.Services.Licensing;

public interface ILicenseStore
{
    Task<LicenseStoreReadResult> ReadCurrentAsync(CancellationToken cancellationToken = default);
}
