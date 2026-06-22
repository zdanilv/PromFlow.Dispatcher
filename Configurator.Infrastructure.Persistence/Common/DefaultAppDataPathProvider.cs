using Configurator.Application.Services.Archiving;

namespace Configurator.Infrastructure.Persistence.Common;

public sealed class DefaultAppDataPathProvider : IAppDataPathProvider
{
    private const string ApplicationDirectoryName = "PromFlow.Dispatcher";
    private const string ArchiveDirectoryName = "Archive";
    private const string ExportDirectoryName = "Exports";

    public string GetArchiveBaseDirectory(ArchiveOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrWhiteSpace(options.BaseDirectory))
        {
            return Path.GetFullPath(options.BaseDirectory);
        }

        return Path.Combine(GetLocalApplicationDataDirectory(), ApplicationDirectoryName, ArchiveDirectoryName);
    }

    public string GetArchiveExportDirectory(ArchiveOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrWhiteSpace(options.ExportDirectory))
        {
            return Path.GetFullPath(options.ExportDirectory);
        }

        return Path.Combine(GetArchiveBaseDirectory(options), ExportDirectoryName);
    }

    private static string GetLocalApplicationDataDirectory()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return string.IsNullOrWhiteSpace(localApplicationData) ? Path.GetTempPath() : localApplicationData;
    }
}
