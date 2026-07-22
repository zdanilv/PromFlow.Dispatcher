using Configurator.Application.Services;
using Configurator.Infrastructure.Services.Autostart;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests.Autostart;

public sealed class AutostartRegistrationTests
{
    [Fact]
    public void Windows_backend_quotes_executable_and_removes_registration()
    {
        var registry = new RecordingWindowsRunRegistry();
        var backend = new WindowsAutostartRegistrationBackend(registry);
        const string executable = @"C:\Program Files\PromFlow Dispatcher\PromFlow.Dispatcher.exe";

        backend.Synchronize(executable, isEnabled: true, launchInTerminal: false);

        Assert.Equal(WindowsAutostartRegistrationBackend.ValueName, registry.SetName);
        Assert.Equal($"\"{executable}\"", registry.SetCommand);

        backend.Synchronize(executable, isEnabled: false, launchInTerminal: false);

        Assert.Equal(WindowsAutostartRegistrationBackend.ValueName, registry.DeletedName);
    }

    [Fact]
    public void Linux_backend_creates_terminal_desktop_entry_and_removes_it()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PromFlow-autostart-{Guid.NewGuid():N}");
        var entryPath = Path.Combine(directory, "autostart", "promflow-dispatcher.desktop");
        try
        {
            var backend = new LinuxAutostartRegistrationBackend(entryPath);

            backend.Synchronize("/opt/PromFlow Dispatcher/PromFlow.Dispatcher", isEnabled: true, launchInTerminal: true);

            var content = File.ReadAllText(entryPath);
            Assert.Contains("Exec=\"/opt/PromFlow Dispatcher/PromFlow.Dispatcher\"", content);
            Assert.Contains("Terminal=true", content);
            Assert.Contains("X-GNOME-Autostart-enabled=true", content);

            backend.Synchronize("/opt/PromFlow Dispatcher/PromFlow.Dispatcher", isEnabled: false, launchInTerminal: true);

            Assert.False(File.Exists(entryPath));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Service_uses_enabled_setting_and_admin_terminal_mode()
    {
        var backend = new RecordingBackend();
        var service = new ApplicationAutostartRegistrationService(
            Options.Create(new StartupOptions { Enabled = false }),
            Options.Create(new ApplicationOptions { WorkMode = ApplicationOptions.AdminWorkMode }),
            [backend],
            new FixedExecutablePathProvider("/opt/promflow-dispatcher/PromFlow.Dispatcher"),
            NullLogger<ApplicationAutostartRegistrationService>.Instance);

        service.Synchronize();

        Assert.Equal("/opt/promflow-dispatcher/PromFlow.Dispatcher", backend.ExecutablePath);
        Assert.False(backend.IsEnabled);
        Assert.True(backend.LaunchInTerminal);
    }

    private sealed class RecordingWindowsRunRegistry : IWindowsRunRegistry
    {
        public string? SetName { get; private set; }
        public string? SetCommand { get; private set; }
        public string? DeletedName { get; private set; }

        public void SetValue(string name, string command)
        {
            SetName = name;
            SetCommand = command;
        }

        public void DeleteValue(string name) => DeletedName = name;
    }

    private sealed class RecordingBackend : IAutostartRegistrationBackend
    {
        public bool IsSupported => true;
        public string? ExecutablePath { get; private set; }
        public bool IsEnabled { get; private set; }
        public bool LaunchInTerminal { get; private set; }

        public void Synchronize(string executablePath, bool isEnabled, bool launchInTerminal)
        {
            ExecutablePath = executablePath;
            IsEnabled = isEnabled;
            LaunchInTerminal = launchInTerminal;
        }
    }

    private sealed class FixedExecutablePathProvider(string executablePath) : IApplicationExecutablePathProvider
    {
        public string? GetExecutablePath() => executablePath;
    }
}
