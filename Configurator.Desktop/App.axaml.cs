using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Configurator.Application.Services;
using Configurator.Application.Services.History;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Desktop.Main;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Configurator.Desktop;

public partial class App : Avalonia.Application
{
    public static IServiceProvider Services { get; set; } = null!;
    private bool _isShutdownInProgress;
    private bool _isShutdownAllowed;
    private IHistoryArchiveService? _historyArchive;
    private RouteMapHistoryCollector? _historyCollector;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Services.GetService<IAutostartRegistrationService>()?.Synchronize();
            StartHistory();
            var window = Services.GetRequiredService<MainWindow>();
            window.DataContext = Services.GetRequiredService<MainViewModel>(); // “проводок” здесь
            window.Closing += OnMainWindowClosing;
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isShutdownAllowed)
        {
            return;
        }

        e.Cancel = true;

        if (_isShutdownInProgress)
        {
            return;
        }

        _isShutdownInProgress = true;

        if (_historyArchive is not null)
            _historyArchive.StatusChanged -= OnHistoryStorageAvailable;
        StopHistoryProducer(Services.GetService<MainViewModel>());
        var signalProvider = Services.GetService<ISignalValueProvider>();
        StopHistoryProducer(signalProvider as IDisposable);
        await StopModbusRuntimeAsync();
        StopHistoryProducer(_historyCollector);
        if (signalProvider is RouteMapSignalRuntime runtime)
            await DrainDispatchHistoryAsync(runtime);
        await StopHistoryAsync();
        _isShutdownAllowed = true;

        if (sender is MainWindow window)
        {
            window.Close();
        }
        else if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private static async Task DrainDispatchHistoryAsync(RouteMapSignalRuntime runtime)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await runtime.WaitForPendingDispatchesAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            Services.GetService<ILogger<App>>()?.LogWarning("Some command dispatches were still pending before the history queue closed.");
        }
    }

    private static void StopHistoryProducer(IDisposable? producer)
    {
        try { producer?.Dispose(); }
        catch (Exception ex)
        {
            Services.GetService<ILogger<App>>()?.LogError(ex,
                "Failed to stop a history producer during shutdown: {ProducerType}.", producer?.GetType().Name);
        }
    }

    private static async Task StopModbusRuntimeAsync()
    {
        var runtime = Services.GetService<IModbusRuntimeService>();
        var demoFacade = Services.GetService<IModbusDemoTcpService>();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            if (demoFacade is not null)
            {
                await demoFacade.StopAsync(timeout.Token);
            }
            else if (runtime is not null)
            {
                await runtime.StopAsync(timeout.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[App] Modbus shutdown timeout.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Modbus shutdown failed: {ex}");
        }
    }

    private void StartHistory()
    {
        _historyArchive = Services.GetService<IHistoryArchiveService>();
        if (_historyArchive is not { } archive)
            return;

        // Startup opens the DB and acquires the lease before any window or workspace starts its producers.
        Task.Run(() => archive.StartAsync()).GetAwaiter().GetResult();
        if (archive.CurrentStatus.State == HistoryArchiveState.StorageInUse)
        {
            archive.StatusChanged += OnHistoryStorageAvailable;
            OnHistoryStorageAvailable(archive, archive.CurrentStatus);
        }
        else
        {
            StartHistoryCollector();
        }
    }

    private void OnHistoryStorageAvailable(object? sender, HistoryArchiveStatus status)
    {
        if (!status.StorageReady)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_isShutdownInProgress)
                return;
            if (_historyArchive is not null)
                _historyArchive.StatusChanged -= OnHistoryStorageAvailable;
            StartHistoryCollector();
        });
    }

    private void StartHistoryCollector()
    {
        _historyCollector ??= Services.GetRequiredService<RouteMapHistoryCollector>();
        _historyCollector.Start();
    }

    private async Task StopHistoryAsync()
    {
        if (_historyArchive is not { } archive)
            return;

        var logger = Services.GetService<ILogger<App>>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        try
        {
            await archive.StopAsync(timeout.Token).WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            logger?.LogWarning("Timed out stopping the history archive service.");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to stop the history archive service.");
        }
    }
}
