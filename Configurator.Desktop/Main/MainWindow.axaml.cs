
using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Configurator.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI.Avalonia;
using System.Diagnostics;

namespace Configurator.Desktop.Main;

/// <summary>
/// Главное окно приложения. Применяет и сохраняет настройки окна (размер, полноэкранный режим).
/// </summary>
public partial class MainWindow : ReactiveWindow<MainViewModel>
{
    private readonly IAppConfigService _configService;
    private UserSettings _userSettings;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // Получаем сервис конфигурации через DI и загружаем пользовательские настройки.
        _configService = Configurator.Desktop.App.Services.GetRequiredService<IAppConfigService>();
        _userSettings = _configService.LoadUserSettings();

        // Avalonia определит доступный экран после подключения окна к desktop lifetime.
        // Это работает и на Windows, и на X11/XWayland, где Screens.Primary в конструкторе
        // окна ещё может быть null.
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

#if DEBUG
        Debug.WriteLine($"[MainWindow] Загружены UserSettings: Width={_userSettings.Width}, Height={_userSettings.Height}, FullScreen={_userSettings.IsFullScreen}");
#endif
        // Корректируем размер и позицию, если FullScreen
        if (_userSettings.IsFullScreen)
        {
            this.WindowState = WindowState.FullScreen;
        }
        else
        {
            this.Width = _userSettings.Width;
            this.Height = _userSettings.Height;
            this.WindowState = WindowState.Normal;
        }

        // Подписка на событие закрытия окна для сохранения настроек
        this.Closing += OnWindowClosing;
    }

        /// <summary>
        /// Сохраняет текущие параметры окна в пользовательский конфиг при закрытии.
        /// </summary>
        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            var settings = _configService.LoadUserSettings();
            settings.Width = this.Width;
            settings.Height = this.Height;
            settings.IsFullScreen = this.WindowState == WindowState.FullScreen;
#if DEBUG
            Debug.WriteLine($"[MainWindow] Сохраняются UserSettings: Width={settings.Width}, Height={settings.Height}, FullScreen={settings.IsFullScreen}");
#endif
            _configService.SaveUserSettings(settings);
        }
}
