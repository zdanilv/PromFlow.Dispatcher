using System.Reflection;
using System.Reactive.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Configurator.Application.Services;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Main;
using DialogHostAvalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Configurator.Tests.RouteMap.Ui;

public sealed class MainWindowLifecycleTests
{
    [AvaloniaTheory]
    [InlineData("io", false)]
    [InlineData("access", false)]
    [InlineData("json", false)]
    [InlineData("io", true)]
    [InlineData("access", true)]
    [InlineData("json", true)]
    public async Task Closing_SettingsFailure_StillDisposesAndStopsActiveRuntimeExactlyOnce(string failure, bool failRead)
    {
        using var styles = new DialogStyles();
        var expected = CreateFailure(failure);
        var settings = new WindowSettingsService();
        var sequence = new List<string>();
        var signals = new DisposableSignals(sequence);
        var runtime = new DeferredActiveRuntime(sequence);
        var logger = new RecordingLogger();
        using var services = new ServiceCollection()
            .AddSingleton<IAppConfigService>(settings)
            .AddSingleton<ISignalValueProvider>(signals)
            .AddSingleton<IModbusRuntimeService>(runtime)
            .AddSingleton<ILogger<MainWindow>>(logger)
            .BuildServiceProvider();
        var originalServices = Configurator.Desktop.App.Services;
        Configurator.Desktop.App.Services = services;
        var app = new Configurator.Desktop.App();
        MainWindow? window = null;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            window = new MainWindow();
            // Keep the production subscription order: window settings first, App shutdown second.
            var method = typeof(Configurator.Desktop.App).GetMethod("OnMainWindowClosing", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var shutdown = method.CreateDelegate<EventHandler<WindowClosingEventArgs>>(app);
            window.Closing += shutdown;
            var closingCalls = 0;
            window.Closing += (_, _) => closingCalls++;
            window.Closed += (_, _) =>
            {
                sequence.Add("closed");
                closed.TrySetResult();
            };
            window.Show();
            if (failRead) settings.ReadFailure = expected;
            else settings.SaveFailure = expected;

            Assert.Equal(ModbusConnectionState.Running, runtime.Status.ClientState);
            window.Close();
            await runtime.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(1, closingCalls);
            Assert.True(window.IsVisible);
            Assert.False(closed.Task.IsCompleted);
            Assert.Equal(1, signals.DisposeCount);
            Assert.Equal(1, runtime.StopCount);
            Assert.Equal(new[] { "dispose", "stop" }, sequence);
            Assert.Same(expected, Assert.Single(logger.Exceptions));

            // A second close while stop is pending must only re-enter the existing guard.
            window.Close();
            Assert.Equal(2, closingCalls);
            Assert.Equal(1, runtime.StopCount);
            Assert.Equal(1, signals.DisposeCount);
            Assert.False(closed.Task.IsCompleted);

            runtime.CompleteStop();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.False(window.IsVisible);
            Assert.Equal(ModbusConnectionState.Stopped, runtime.Status.ClientState);
            Assert.Equal(3, closingCalls); // The final close is allowed by App's existing protocol.
            Assert.Equal(1, runtime.StopCount);
            Assert.Equal(1, signals.DisposeCount);
            Assert.Equal(new[] { "dispose", "stop", "closed" }, sequence);
            Assert.Equal(3, logger.Exceptions.Count);
            Assert.All(logger.Exceptions, error => Assert.Same(expected, error));
        }
        finally
        {
            runtime.CompleteStop();
            if (window is { IsVisible: true })
            {
                settings.ReadFailure = null;
                settings.SaveFailure = null;
                window.Close();
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }
            Configurator.Desktop.App.Services = originalServices;
        }
    }

    [AvaloniaTheory]
    [InlineData(false, 1200, 800)]
    [InlineData(false, 1366, 768)]
    [InlineData(true, 1366, 768)]
    public void Window_StartsMaximizedOrFullScreen_AndSavesSettings(bool fullScreen, double savedWidth, double savedHeight)
    {
        using var styles = new DialogStyles();
        var settings = new WindowSettingsService
        {
            Settings = new UserSettings { Width = savedWidth, Height = savedHeight, IsFullScreen = fullScreen }
        };
        using var services = new ServiceCollection().AddSingleton<IAppConfigService>(settings).BuildServiceProvider();
        var originalServices = Configurator.Desktop.App.Services;
        Configurator.Desktop.App.Services = services;
        try
        {
            var window = new MainWindow();
            Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
            var expectedState = fullScreen ? WindowState.FullScreen : WindowState.Maximized;
            Assert.Equal(expectedState, window.WindowState);
            if (!fullScreen)
            {
                Assert.Equal(savedWidth, window.Width);
                Assert.Equal(savedHeight, window.Height);
            }
            window.Show();
            Assert.Equal(expectedState, window.WindowState);
            var width = window.Width;
            var height = window.Height;
            window.Close();
            Assert.Equal(width, settings.Saved!.Width);
            Assert.Equal(height, settings.Saved.Height);
            Assert.Equal(fullScreen, settings.Saved.IsFullScreen);
        }
        finally { Configurator.Desktop.App.Services = originalServices; }
    }

    [AvaloniaFact]
    public void Window_ClosedInNormalState_StartsMaximizedAgain()
    {
        using var styles = new DialogStyles();
        var settings = new WindowSettingsService();
        using var services = new ServiceCollection().AddSingleton<IAppConfigService>(settings).BuildServiceProvider();
        var originalServices = Configurator.Desktop.App.Services;
        Configurator.Desktop.App.Services = services;
        MainWindow? window = null;
        try
        {
            window = new MainWindow();
            Assert.Equal(WindowState.Maximized, window.WindowState);
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Width = 1000;
            window.Height = 700;
            window.Close();
            Assert.False(settings.Saved!.IsFullScreen);

            window = new MainWindow();
            Assert.Equal(WindowState.Maximized, window.WindowState);
            Assert.Equal(1000, window.Width);
            Assert.Equal(700, window.Height);
            window.Show();
            Assert.Equal(WindowState.Maximized, window.WindowState);
        }
        finally
        {
            window?.Close();
            Configurator.Desktop.App.Services = originalServices;
        }
    }

    [AvaloniaFact]
    public void Closing_UnrelatedFailure_IsNotSuppressed()
    {
        using var styles = new DialogStyles();
        var settings = new WindowSettingsService();
        using var services = new ServiceCollection().AddSingleton<IAppConfigService>(settings).BuildServiceProvider();
        var originalServices = Configurator.Desktop.App.Services;
        Configurator.Desktop.App.Services = services;
        MainWindow? window = null;
        try
        {
            window = new MainWindow();
            window.Show();
            var expected = new InvalidOperationException("unrelated programming failure");
            settings.SaveFailure = expected;
            Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => window.Close()));
        }
        finally
        {
            settings.SaveFailure = null;
            window?.Close();
            Configurator.Desktop.App.Services = originalServices;
        }
    }

    private static Exception CreateFailure(string kind) => kind switch
    {
        "io" => new IOException("injected window settings I/O failure"),
        "access" => new UnauthorizedAccessException("injected window settings permission failure"),
        "json" => new JsonException("injected malformed window settings"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private sealed class DialogStyles : IDisposable
    {
        private readonly DialogHostStyles _styles = new();
        public DialogStyles() => Avalonia.Application.Current!.Styles.Add(_styles);
        public void Dispose() => Avalonia.Application.Current!.Styles.Remove(_styles);
    }

    private sealed class WindowSettingsService : IAppConfigService
    {
        public UserSettings Settings { get; init; } = new();
        public Exception? ReadFailure { get; set; }
        public Exception? SaveFailure { get; set; }
        public UserSettings? Saved { get; private set; }
        public T GetSection<T>(string sectionName) where T : class, new() => new();
        public string GetValue(string key) => string.Empty;
        public Task SaveSectionAsync<T>(string sectionName, T value, CancellationToken ct = default) => Task.CompletedTask;
        public UserSettings LoadUserSettings() => ReadFailure is { } error ? throw error : Settings;
        public void SaveUserSettings(UserSettings settings)
        {
            if (SaveFailure is { } error) throw error;
            Saved = settings;
        }
    }

    private sealed class RecordingLogger : ILogger<MainWindow>
    {
        public List<Exception?> Exceptions { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Assert.Equal(LogLevel.Warning, logLevel);
            Exceptions.Add(exception);
        }
    }

    private sealed class DisposableSignals(List<string> sequence) : ISignalValueProvider, IDisposable
    {
        public int DisposeCount { get; private set; }
        public IObservable<IReadOnlyDictionary<string, SignalValue>> Observe() => Observable.Never<IReadOnlyDictionary<string, SignalValue>>();
        public void Dispose() { DisposeCount++; sequence.Add("dispose"); }
    }

    private sealed class DeferredActiveRuntime(List<string> sequence) : IModbusRuntimeService
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StopCount { get; private set; }
        public ModbusStatus Status { get; private set; } = new() { ClientState = ModbusConnectionState.Running };
        public ModbusOptions CurrentOptions { get; } = new();
        public ModbusSnapshot ClientSnapshot => ModbusSnapshot.Empty;
        public ModbusSnapshot ServerSnapshot => ModbusSnapshot.Empty;
        public event EventHandler<ModbusStatus>? StatusChanged { add { } remove { } }
        public event EventHandler<ModbusSnapshot>? SnapshotChanged { add { } remove { } }
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            Assert.False(cancellationToken.IsCancellationRequested);
            StopCount++;
            sequence.Add("stop");
            StopEntered.TrySetResult();
            await _release.Task;
            Status = ModbusStatus.Stopped;
        }
        public void CompleteStop() => _release.TrySetResult();
        public Task StartAsync(ModbusRunMode mode = ModbusRunMode.Both, ModbusOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RestartAsync(ModbusRunMode mode = ModbusRunMode.Both, ModbusOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopClientAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RestartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopServerAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RestartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
