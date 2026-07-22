namespace Configurator.Infrastructure.Services.Autostart;

public sealed class WindowsAutostartRegistrationBackend(IWindowsRunRegistry registry) : IAutostartRegistrationBackend
{
    public const string ValueName = "PromFlow Dispatcher";

    public bool IsSupported => OperatingSystem.IsWindows();

    public void Synchronize(string executablePath, bool isEnabled, bool launchInTerminal)
    {
        if (isEnabled)
        {
            registry.SetValue(ValueName, Quote(executablePath));
            return;
        }

        registry.DeleteValue(ValueName);
    }

    private static string Quote(string executablePath) =>
        $"\"{executablePath.Replace("\"", "\\\"")}\"";
}
