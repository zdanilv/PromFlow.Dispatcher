namespace Configurator.Application.Services;

/// <summary>
/// Синхронизирует системную регистрацию автозапуска с настройкой приложения.
/// </summary>
public interface IAutostartRegistrationService
{
    void Synchronize();
}
