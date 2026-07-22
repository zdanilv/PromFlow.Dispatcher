namespace Configurator.Application.Services;

/// <summary>
/// Возвращает актуальные настройки контактов для диалога помощи.
/// </summary>
public interface IHelpOptionsProvider
{
    HelpOptions GetCurrent();
}
