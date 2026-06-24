using Configurator.Application.Services;
using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Configurator.Infrastructure
{
    public static class DependencyInjection
    {
        // IConfiguration configuration - pass configuration from Boot.
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            // Services.
            services.AddSingleton<Configurator.Infrastructure.Services.AppConfigService>();
            services.AddSingleton<IAppConfigService>(serviceProvider =>
                new AuthorizedAppConfigService(
                    serviceProvider.GetRequiredService<Configurator.Infrastructure.Services.AppConfigService>(),
                    serviceProvider.GetRequiredService<IAccessDecisionService>()));
            services.AddSingleton<IConfiguration>(configuration);
            return services;
        }
    }
}
