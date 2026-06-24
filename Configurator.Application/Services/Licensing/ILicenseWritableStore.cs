namespace Configurator.Application.Services.Licensing;

public interface ILicenseWritableStore
{
    Task<LicenseStoreWriteResult> ReplaceCurrentAsync(
        byte[] licenseBytes,
        CancellationToken cancellationToken = default);
}
