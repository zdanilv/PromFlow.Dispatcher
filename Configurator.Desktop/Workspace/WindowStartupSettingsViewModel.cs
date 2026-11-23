using System.Reactive;
using System.Reactive.Linq;
using System.Text.Json;
using Configurator.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReactiveUI;

namespace Configurator.Desktop.Workspace;

public sealed class WindowStartupSettingsViewModel : ViewModelBase
{
    private readonly IAppConfigService _configService;
    private readonly bool _isAdminMode;
    private readonly ILogger<WindowStartupSettingsViewModel> _logger;
    private bool _startFullScreen;
    private string? _saveMessage;

    public WindowStartupSettingsViewModel(
        IAppConfigService configService,
        IOptions<ApplicationOptions> applicationOptions,
        ILogger<WindowStartupSettingsViewModel> logger)
    {
        _configService = configService;
        _isAdminMode = applicationOptions.Value.IsAdminMode;
        _logger = logger;
        _startFullScreen = _isAdminMode && configService.LoadUserSettings().IsFullScreen;
        SaveCommand = ReactiveCommand.Create(Save, Observable.Return(_isAdminMode));
    }

    public bool StartFullScreen
    {
        get => _startFullScreen;
        set
        {
            if (_startFullScreen == value)
                return;

            this.RaiseAndSetIfChanged(ref _startFullScreen, value);
            SaveMessage = null;
        }
    }

    public string? SaveMessage
    {
        get => _saveMessage;
        private set => this.RaiseAndSetIfChanged(ref _saveMessage, value);
    }

    public ReactiveCommand<Unit, Unit> SaveCommand { get; }

    private void Save()
    {
        if (!_isAdminMode)
            return;

        try
        {
            var settings = _configService.LoadUserSettings();
            settings.IsFullScreen = StartFullScreen;
            _configService.SaveUserSettings(settings);
            SaveMessage = "Настройка применится при следующем запуске.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to save the full-screen startup setting");
            SaveMessage = "Не удалось сохранить настройку полноэкранного запуска.";
        }
    }
}
