using Configurator.Application.Services;
using Microsoft.Extensions.Configuration;

namespace Configurator.Infrastructure.Services;

/// <summary>
/// Выбирает полный раздел Help из пользовательской конфигурации, если он задан.
/// Это предотвращает поэлементное смешивание JSON-массивов с defaults.
/// </summary>
public sealed class HelpOptionsProvider(
    IConfiguration installedConfiguration,
    IConfiguration userConfiguration) : IHelpOptionsProvider
{
    public HelpOptions GetCurrent()
    {
        var userSection = userConfiguration.GetSection(HelpOptions.SectionName);
        var source = userSection.Exists()
            ? userSection
            : installedConfiguration.GetSection(HelpOptions.SectionName);

        return source.Get<HelpOptions>() ?? new HelpOptions();
    }
}
