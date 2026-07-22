namespace Configurator.Application.Services;

/// <summary>
/// Настройки запуска приложения вместе с пользовательской сессией ОС.
/// </summary>
public sealed class StartupOptions
{
    public const string SectionName = "Startup";

    public bool Enabled { get; set; } = true;
}
