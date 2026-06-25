using Configurator.Application.Services.Runtime;

namespace Configurator.Infrastructure.Runtime;

public sealed class ApplicationRuntimePathProvider
{
    private const string ApplicationDirectoryName = "PromFlow.Dispatcher";
    private const string RuntimeDirectoryName = "runtime";
    private readonly ApplicationLifecycleOptions _options;

    public ApplicationRuntimePathProvider(ApplicationLifecycleOptions options)
    {
        _options = options?.Clone() ?? throw new ArgumentNullException(nameof(options));
    }

    public string GetRuntimeDirectory()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var baseDirectory = string.IsNullOrWhiteSpace(localApplicationData)
            ? Path.GetTempPath()
            : localApplicationData;

        return Path.Combine(baseDirectory, ApplicationDirectoryName, RuntimeDirectoryName);
    }

    public string GetLockFilePath()
    {
        var fileName = string.IsNullOrWhiteSpace(_options.LockFileName)
            ? "promflow-dispatcher.lock"
            : _options.LockFileName.Trim();

        return Path.IsPathRooted(fileName)
            ? Path.GetFullPath(fileName)
            : Path.Combine(GetRuntimeDirectory(), fileName);
    }
}
