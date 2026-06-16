
using Avalonia;
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

        // Получаем сервис конфигурации через DI
        _configService = (IAppConfigService)Configurator.Desktop.App.Services.GetService(typeof(IAppConfigService))!;
        // Загружаем пользовательские настройки через интерфейс
        _userSettings = _configService != null ? _configService.LoadUserSettings() : new UserSettings();

#if DEBUG
        Debug.WriteLine($"[MainWindow] Загружены UserSettings: Width={_userSettings.Width}, Height={_userSettings.Height}, FullScreen={_userSettings.IsFullScreen}");
#endif
        // Корректируем размер и позицию, если FullScreen
        if (_userSettings.IsFullScreen)
        {
            this.WindowState = WindowState.FullScreen;
            this.Position = new PixelPoint(0, 0);
        }
        else
        {
            this.Width = _userSettings.Width;
            this.Height = _userSettings.Height;
            var screen = Screens.Primary;
            int x = (int)((screen.WorkingArea.Width - this.Width) / 2);
            int y = (int)((screen.WorkingArea.Height - this.Height) / 2);
            this.Position = new PixelPoint(x, y);
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
            if (_configService != null)
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
}
