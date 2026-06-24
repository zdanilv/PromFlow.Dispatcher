namespace Configurator.Application.Services.Licensing;

public sealed record LicenseInstallRequest(
    byte[] LicenseBytes,
    string? SourceFileName = null);
