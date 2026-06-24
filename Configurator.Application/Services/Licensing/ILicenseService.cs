namespace Configurator.Application.Services.Licensing;

public interface ILicenseService
{
    Task<LicenseState> GetCurrentAsync(CancellationToken cancellationToken = default);

    Task<LicenseValidationResult> VerifyAsync(byte[] licenseBytes, CancellationToken cancellationToken = default);
}
