using Configurator.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Configurator.Infrastructure.Services.Autostart;

public sealed class ApplicationAutostartRegistrationService(
    IOptions<StartupOptions> startupOptions,
    IOptions<ApplicationOptions> applicationOptions,
    IEnumerable<IAutostartRegistrationBackend> backends,
    IApplicationExecutablePathProvider executablePathProvider,
    ILogger<ApplicationAutostartRegistrationService> logger) : IAutostartRegistrationService
{
    public void Synchronize()
    {
        var backend = backends.FirstOrDefault(candidate => candidate.IsSupported);
        if (backend is null)
        {
            logger.LogDebug("Autostart registration is not supported on this platform.");
            return;
        }

        var executablePath = executablePathProvider.GetExecutablePath();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            logger.LogWarning("Autostart registration skipped because the executable path is unavailable.");
            return;
        }

        try
        {
            backend.Synchronize(
                executablePath,
                startupOptions.Value.Enabled,
                applicationOptions.Value.IsAdminMode);
            logger.LogInformation(
                "Autostart registration was synchronized. Enabled: {Enabled}.",
                startupOptions.Value.Enabled);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to synchronize autostart registration.");
        }
    }
}
