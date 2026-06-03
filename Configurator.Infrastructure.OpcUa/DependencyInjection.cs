using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;
using Configurator.Infrastructure.OpcUa.Client;
using Configurator.Infrastructure.OpcUa.Runtime;
using Configurator.Infrastructure.OpcUa.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Configurator.Infrastructure.OpcUa;

/// <summary>
/// Регистрирует инфраструктурные сервисы OPC UA в контейнере зависимостей.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Регистрирует клиент, сервер, browser и runtime-сервисы OPC UA.
    /// </summary>
    public static IServiceCollection AddOpcUaInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<OpcUaOptions>(configuration.GetSection(OpcUaOptions.SectionName));

        services.AddSingleton<IOpcUaTagWriteRequestValidator, OpcUaTagWriteRequestValidator>();
        services.AddSingleton<IOpcUaTagConfigurationValidator, OpcUaTagConfigurationValidator>();
        services.AddSingleton<IOpcUaSecurityProvider, DisabledOpcUaSecurityProvider>();
        services.AddSingleton<IOpcUaIdentityProvider, AnonymousOpcUaIdentityProvider>();
        services.AddSingleton<OpcUaApplicationConfigurationFactory>();
        services.AddSingleton<OpcUaServerCommandState>();
        services.AddSingleton<IOpcUaServerCommandStateProvider>(sp => sp.GetRequiredService<OpcUaServerCommandState>());

        // Эти поставщики задают базовый режим None/Anonymous,
        // чтобы клиент, сервер и browser использовали одинаковую модель безопасности.
        services.AddSingleton<OpcUaClientService>();
        services.AddSingleton<IOpcUaClientService>(sp => sp.GetRequiredService<OpcUaClientService>());
        services.AddSingleton<IOpcUaTagWriter>(sp => sp.GetRequiredService<OpcUaClientService>());
        services.AddSingleton<IOpcUaConnectionStateProvider>(sp => sp.GetRequiredService<OpcUaClientService>());
        services.AddSingleton<IOpcUaTagBrowserService, OpcUaTagBrowserService>();

        // Один экземпляр серверного состояния нужен двум сценариям: lifecycle API и публикации command-тегов.
        services.AddSingleton<OpcUaServerService>();
        services.AddSingleton<IOpcUaServerService>(sp => sp.GetRequiredService<OpcUaServerService>());
        services.AddSingleton<IOpcUaServerTagUpdater>(sp => sp.GetRequiredService<OpcUaServerService>());

        services.AddSingleton<IOpcUaRuntimeService, OpcUaRuntimeService>();

        return services;
    }
}
