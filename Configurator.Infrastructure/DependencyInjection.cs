using Configurator.Application.Services.Authorization;
using Configurator.Application.Services;
using Configurator.Infrastructure.Services;
using Configurator.Infrastructure.Services.Authorization;
using Configurator.Infrastructure.Services.Autostart;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Configurator.Infrastructure
{
    public static class DependencyInjection
    {
        // IConfiguration configuration - проброс конфигурации из Boot
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration) =>
            AddInfrastructure(services, configuration, writableConfiguration: null);

        public static IServiceCollection AddInfrastructure(
            this IServiceCollection services, IConfiguration configuration, IConfigurationRoot? writableConfiguration)
        {
            // Реализация интерфейсов
            //services.AddScoped<IJsonStartspaceOptions, JsonStartspaceOptions>();
            //services.AddScoped<IGetRecentGroupCase, GetRecentGroupCase>();
            //services.AddScoped<IDialogService, DialogService>();
            // Сервисы
            //services.AddSingleton<IProjectService, ProjectService>();
            services.AddSingleton<IAuthApp, AuthApp>();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<IWindowsRunRegistry, WindowsRunRegistry>();
            services.AddSingleton<IAutostartRegistrationBackend, WindowsAutostartRegistrationBackend>();
            services.AddSingleton<IAutostartRegistrationBackend, LinuxAutostartRegistrationBackend>();
            services.AddSingleton<IApplicationExecutablePathProvider, RuntimeExecutablePathProvider>();
            services.AddSingleton<IAutostartRegistrationService, ApplicationAutostartRegistrationService>();
            services.AddSingleton<Configurator.Application.Services.IAppConfigService>(sp => new AppConfigService(
                sp.GetRequiredService<IConfiguration>(),
                ApplicationConfigPaths.SharedAppSettingsPath,
                ApplicationConfigPaths.UserSettingsPath,
                legacyUserSettingsFilePaths: null,
                writableConfiguration: writableConfiguration)
            {
                Logger = sp.GetService<ILogger<AppConfigService>>() ?? NullLogger<AppConfigService>.Instance
            });
            return services;
        }
    }
}
