using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Configurator.Boot.Console;

internal static class ConsoleVisibilityController
{
    public const string AdminConsoleHostArgument = "--promflow-admin-console-host";
    private const uint AttachParentProcess = 0xFFFF_FFFF;
    private const int ErrorAccessDenied = 5;

    public static bool TryRelaunchLinuxAdminProcess(
        bool isAdminMode,
        IReadOnlyList<string> arguments,
        out Exception? failure)
    {
        failure = null;
        if (!OperatingSystem.IsLinux() ||
            !isAdminMode ||
            arguments.Contains(AdminConsoleHostArgument, StringComparer.Ordinal) ||
            HasInteractiveConsole())
        {
            return false;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            failure = new InvalidOperationException("The current executable path is unavailable for xterm launch.");
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo("xterm") { UseShellExecute = false };
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add(executablePath);
            startInfo.ArgumentList.Add(AdminConsoleHostArgument);
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            if (Process.Start(startInfo) is null)
            {
                failure = new InvalidOperationException("xterm did not start the admin process.");
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            failure = exception;
            return false;
        }
    }

    public static void ConfigureWindowsConsole(bool isAdminMode)
    {
        if (!OperatingSystem.IsWindows() || !isAdminMode)
            return;

        if (GetConsoleWindow() == IntPtr.Zero)
        {
            var attached = AttachConsole(AttachParentProcess);
            if (!attached && Marshal.GetLastWin32Error() != ErrorAccessDenied)
                _ = AllocConsole();
        }

        try
        {
            System.Console.SetOut(new StreamWriter(System.Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
            System.Console.SetError(new StreamWriter(System.Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
        }
        catch (IOException)
        {
            // File logging remains available if standard handles cannot be redirected.
        }
    }

    public static string[] RemoveInternalArguments(IEnumerable<string> arguments) =>
        arguments
            .Where(argument => !string.Equals(argument, AdminConsoleHostArgument, StringComparison.Ordinal))
            .ToArray();

    private static bool HasInteractiveConsole() =>
        !System.Console.IsInputRedirected &&
        !System.Console.IsOutputRedirected &&
        !System.Console.IsErrorRedirected;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();
}
