using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Licensing;
using Configurator.Application.Services.Modbus.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace Configurator.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            // MediatR/FluentValidation/Mapster can be added here later.
            //services.AddScoped<IGetRecentGroupCase, GetRecentGroupCase>();
            services.AddSingleton<DefaultLicenseService>();
            services.AddSingleton<ILicenseService>(sp => sp.GetRequiredService<DefaultLicenseService>());
            services.AddSingleton<ILicenseStateAccessor>(sp => sp.GetRequiredService<DefaultLicenseService>());
            services.AddSingleton<ILicenseFeatureGate, LicenseFeatureGate>();
            services.AddSingleton<ILicenseVerifier, OfflineLicenseVerifier>();
            services.AddSingleton<IAccessDecisionService, DefaultAccessDecisionService>();
            services.AddSingleton<IModbusDataMapValidator, ModbusDataMapValidator>();
            return services;
        }
    }
}
