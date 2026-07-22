namespace Configurator.Infrastructure.Services.Autostart;

public interface IApplicationExecutablePathProvider
{
    string? GetExecutablePath();
}
