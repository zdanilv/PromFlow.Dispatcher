using System.Diagnostics;

namespace Configurator.Infrastructure.Services.Autostart;

public sealed class RuntimeExecutablePathProvider : IApplicationExecutablePathProvider
{
    public string? GetExecutablePath() =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
}
