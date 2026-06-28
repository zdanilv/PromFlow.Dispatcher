using Configurator.Application.Services.Authorization;
using Configurator.Infrastructure.Services;
using Configurator.Infrastructure.Services.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Configurator.Infrastructure
{
    public static class DependencyInjection
    {
        // IConfiguration configuration - проброс конфигурации из Boot
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            // Реализация интерфейсов
            //services.AddScoped<IJsonStartspaceOptions, JsonStartspaceOptions>();
            //services.AddScoped<IGetRecentGroupCase, GetRecentGroupCase>();
            //services.AddScoped<IDialogService, DialogService>();
            // Сервисы
            //services.AddSingleton<IProjectService, ProjectService>();
            services.AddSingleton<IAuthApp, AuthApp>();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<Configurator.Application.Services.IAppConfigService>(sp => new AppConfigService(
                sp.GetRequiredService<IConfiguration>(),
                ApplicationConfigPaths.SharedAppSettingsPath));
            return services;
        }
    }
}
