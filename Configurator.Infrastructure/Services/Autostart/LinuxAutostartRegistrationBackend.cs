using System.Text;

namespace Configurator.Infrastructure.Services.Autostart;

public sealed class LinuxAutostartRegistrationBackend : IAutostartRegistrationBackend
{
    private const string DesktopFileName = "PromFlow Dispatcher";
    private readonly string _entryPath;

    public LinuxAutostartRegistrationBackend()
        : this(ApplicationConfigPaths.LinuxAutostartEntryPath)
    {
    }

    public LinuxAutostartRegistrationBackend(string entryPath)
    {
        _entryPath = entryPath;
    }

    public bool IsSupported => OperatingSystem.IsLinux();

    public void Synchronize(string executablePath, bool isEnabled, bool launchInTerminal)
    {
        if (!isEnabled)
        {
            if (File.Exists(_entryPath))
                File.Delete(_entryPath);
            return;
        }

        var directory = Path.GetDirectoryName(_entryPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Не задан каталог XDG Autostart.");

        Directory.CreateDirectory(directory);
        File.WriteAllText(_entryPath, CreateDesktopEntry(executablePath, launchInTerminal), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    internal static string CreateDesktopEntry(string executablePath, bool launchInTerminal) =>
        $"""
        [Desktop Entry]
        Type=Application
        Version=1.0
        Name={DesktopFileName}
        Comment=RouteMap, Modbus TCP and OPC UA dispatcher
        Exec={EscapeExecutablePath(executablePath)}
        Icon=promflow-dispatcher
        Terminal={launchInTerminal.ToString().ToLowerInvariant()}
        Categories=Utility;Engineering;
        StartupNotify=true
        X-GNOME-Autostart-enabled=true
        """;

    private static string EscapeExecutablePath(string executablePath) =>
        $"\"{executablePath.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
