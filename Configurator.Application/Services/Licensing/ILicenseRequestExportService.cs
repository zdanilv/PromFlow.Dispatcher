namespace Configurator.Application.Services.Licensing;

public interface ILicenseRequestExportService
{
    Task<LicenseRequestExportResult> ExportAsync(
        InstallationIdentityRequest request,
        string path,
        CancellationToken cancellationToken = default);
}
