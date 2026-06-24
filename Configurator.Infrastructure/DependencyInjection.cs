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
            services.AddSingleton<Configurator.Application.Services.IAppConfigService, Configurator.Infrastructure.Services.AppConfigService>();
            services.AddSingleton<IConfiguration>(configuration);
            return services;
        }
    }
}
