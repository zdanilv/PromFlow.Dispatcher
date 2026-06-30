using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
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

        await ExportSessionJournalAsync();
        Services.GetService<MainViewModel>()?.Dispose();
        (Services.GetService<ISignalValueProvider>() as IDisposable)?.Dispose();
        await StopModbusRuntimeAsync();
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

    private static async Task ExportSessionJournalAsync()
    {
        var journal = Services.GetService<RouteMapSessionJournal>();
        var exporter = Services.GetService<ISessionJournalExporter>();
        if (journal is null || exporter is null)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await exporter.ExportAsync(journal, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[App] Session journal export timeout.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Session journal export failed: {ex}");
        }
    }
}
