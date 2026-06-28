using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
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
            services.AddSingleton<IModbusDataMapValidator, ModbusDataMapValidator>();
            services.AddSingleton<IModbusAlarmMapValidator, ModbusAlarmMapValidator>();
            return services;
        }
    }
}
