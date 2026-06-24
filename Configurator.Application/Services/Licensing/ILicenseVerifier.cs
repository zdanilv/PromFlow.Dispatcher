namespace Configurator.Application.Services.Licensing;

public interface ILicenseVerifier
{
    Task<LicenseValidationResult> VerifyAsync(byte[] licenseBytes, CancellationToken cancellationToken = default);
}
