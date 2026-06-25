using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Configurator.Desktop.Main;
using Configurator.Desktop.Runtime;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.ComponentModel;
using System.Threading;

namespace Configurator.Desktop;

public partial class App : Avalonia.Application
{
    public static IServiceProvider Services { get; set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = Services.GetRequiredService<MainWindow>();
            window.DataContext = Services.GetRequiredService<MainViewModel>();
            window.Closing += OnMainWindowClosing;
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        var shutdownCoordinator = Services.GetRequiredService<IDesktopShutdownCoordinator>();
        if (shutdownCoordinator.IsCloseAllowed)
        {
            return;
        }

        e.Cancel = true;

        await shutdownCoordinator.RequestShutdownAsync(
            sender as MainWindow,
            ApplicationLifetime as IClassicDesktopStyleApplicationLifetime,
            CancellationToken.None);
    }
}
