namespace Configurator.Infrastructure.Services.Autostart;

/// <summary>
/// Платформенная реализация регистрации запуска приложения при входе пользователя в ОС.
/// </summary>
public interface IAutostartRegistrationBackend
{
    bool IsSupported { get; }

    void Synchronize(string executablePath, bool isEnabled, bool launchInTerminal);
}
