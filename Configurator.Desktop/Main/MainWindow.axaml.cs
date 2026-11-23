
using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Configurator.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI.Avalonia;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Configurator.Desktop.Main;

/// <summary>
/// Главное окно приложения. Применяет настройки запуска и сохраняет размеры окна.
/// </summary>
public partial class MainWindow : ReactiveWindow<MainViewModel>
{
    private readonly IAppConfigService _configService;

    public bool StartedFullScreen { get; }

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // Получаем сервис конфигурации через DI и загружаем пользовательские настройки.
        _configService = Configurator.Desktop.App.Services.GetRequiredService<IAppConfigService>();
        var settings = _configService.LoadUserSettings();

        // Avalonia определит доступный экран после подключения окна к desktop lifetime.
        // Это работает и на Windows, и на X11/XWayland, где Screens.Primary в конструкторе
        // окна ещё может быть null.
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

#if DEBUG
        Debug.WriteLine($"[MainWindow] Загружены UserSettings: Width={settings.Width}, Height={settings.Height}, FullScreen={settings.IsFullScreen}");
#endif
        Width = settings.Width;
        Height = settings.Height;
        StartedFullScreen = settings.IsFullScreen;
        WindowState = StartedFullScreen ? WindowState.FullScreen : WindowState.Maximized;

        // Подписка на событие закрытия окна для сохранения настроек
        this.Closing += OnWindowClosing;
    }

    /// <summary>
    /// Сохраняет текущие размеры окна в пользовательский конфиг при закрытии.
    /// </summary>
    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            var settings = _configService.LoadUserSettings();
            settings.Width = this.Width;
            settings.Height = this.Height;
#if DEBUG
            Debug.WriteLine($"[MainWindow] Сохраняются UserSettings: Width={settings.Width}, Height={settings.Height}, FullScreen={settings.IsFullScreen}");
#endif
            _configService.SaveUserSettings(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            App.Services.GetService<ILogger<MainWindow>>()?.LogWarning(ex,
                "Failed to persist window settings while closing; continuing shutdown");
        }
    }
}
