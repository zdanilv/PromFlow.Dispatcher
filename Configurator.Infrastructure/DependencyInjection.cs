using Configurator.Application.Services;
using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Configuration;
using Configurator.Application.Services.Licensing;
using Configurator.Infrastructure.Licensing;
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
            var licensingOptions = configuration.GetSection(LicensingOptions.SectionName).Get<LicensingOptions>()
                ?? new LicensingOptions();
            services.AddSingleton(licensingOptions);
            if (!services.Any(service => service.ServiceType == typeof(TimeProvider)))
            {
                services.AddSingleton(TimeProvider.System);
            }

            services.AddSingleton<LicensePathProvider>();
            services.AddSingleton<FileLicenseStore>();
            services.AddSingleton<ILicenseStore>(sp => sp.GetRequiredService<FileLicenseStore>());
            services.AddSingleton<ILicenseWritableStore>(sp => sp.GetRequiredService<FileLicenseStore>());
            services.AddSingleton<ILicenseRequestExportService, FileLicenseRequestExportService>();
            services.AddSingleton<IInstallationIdentityService, FileInstallationIdentityService>();
            services.AddSingleton<ITrustedTimeStateStore, FileTrustedTimeStateStore>();
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
