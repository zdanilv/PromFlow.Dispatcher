using Configurator.Application.Services.Authorization;
using Microsoft.Extensions.Options;

namespace Configurator.Infrastructure.Persistence.Security;

public sealed class SecurityDatabasePathProvider
{
    private const string ApplicationDirectoryName = "PromFlow.Dispatcher";
    private const string SecurityDirectoryName = "Security";
    private const string SecurityDatabaseFileName = "promflow-security.sqlite";
    private readonly IOptions<AuthenticationOptions> _options;

    public SecurityDatabasePathProvider(IOptions<AuthenticationOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string GetDatabasePath()
    {
        var configuredPath = _options.Value.SecurityDatabasePath;
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var baseDirectory = string.IsNullOrWhiteSpace(localApplicationData)
            ? Path.GetTempPath()
            : localApplicationData;

        return Path.Combine(
            baseDirectory,
            ApplicationDirectoryName,
            SecurityDirectoryName,
            SecurityDatabaseFileName);
    }
}
