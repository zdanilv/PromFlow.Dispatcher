using Microsoft.Win32;

namespace Configurator.Infrastructure.Services.Autostart;

#pragma warning disable CA1416 // Backend is selected only when OperatingSystem.IsWindows().
public sealed class WindowsRunRegistry : IWindowsRunRegistry
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public void SetValue(string name, string command)
    {
        EnsureWindows();
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Не удалось открыть реестр автозапуска текущего пользователя.");
        key.SetValue(name, command, RegistryValueKind.String);
    }

    public void DeleteValue(string name)
    {
        EnsureWindows();
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Run registry is available only on Windows.");
    }
}
#pragma warning restore CA1416
