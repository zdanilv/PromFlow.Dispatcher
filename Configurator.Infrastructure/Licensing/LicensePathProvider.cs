using Configurator.Application.Services.Licensing;

namespace Configurator.Infrastructure.Licensing;

public sealed class LicensePathProvider
{
    private const string ApplicationDirectoryName = "PromFlow.Dispatcher";
    private const string LicenseDirectoryName = "license";
    private readonly LicensingOptions _options;

    public LicensePathProvider(LicensingOptions options)
    {
        _options = options?.Clone() ?? throw new ArgumentNullException(nameof(options));
    }

    public string GetLicenseDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_options.LicenseDirectory))
        {
            return Path.GetFullPath(_options.LicenseDirectory);
        }

        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var baseDirectory = string.IsNullOrWhiteSpace(localApplicationData)
            ? Path.GetTempPath()
            : localApplicationData;

        return Path.Combine(baseDirectory, ApplicationDirectoryName, LicenseDirectoryName);
    }

    public string GetCurrentLicensePath()
        => ResolveFile(GetLicenseDirectory(), _options.LicenseFileName, "current.promlicense");

    public string GetInstallationIdentityPath()
        => ResolveFile(GetLicenseDirectory(), _options.InstallationIdentityFileName, "installation.json");

    public string GetTrustedTimeStatePath()
        => ResolveFile(GetLicenseDirectory(), _options.TrustedTimeStateFileName, "trusted-time.json");

    private static string ResolveFile(string directory, string configuredFileName, string defaultFileName)
    {
        var fileName = string.IsNullOrWhiteSpace(configuredFileName)
            ? defaultFileName
            : configuredFileName;

        return Path.IsPathRooted(fileName)
            ? Path.GetFullPath(fileName)
            : Path.Combine(directory, fileName);
    }
}
