using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Application.Services.Signals;
using Configurator.Infrastructure.Modbus.Client;
using Configurator.Infrastructure.Modbus.Configuration;
using Configurator.Infrastructure.Modbus.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Configurator.Infrastructure.Modbus;

/// <summary>
/// Регистрирует инфраструктуру Modbus в DI-контейнере.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Добавляет настройки, клиент, сервер, runtime и фасад Modbus.
    /// </summary>
    public static IServiceCollection AddModbusInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ModbusOptions>(configuration.GetSection(ModbusOptions.SectionName));
        services.Configure<RouteMapRuntimeOptions>(configuration.GetSection(RouteMapRuntimeOptions.SectionName));
        services.Configure<ModbusOptions>(
            ModbusOptions.DemoSectionName,
            configuration.GetSection(ModbusOptions.DemoSectionName));

        services.AddSingleton<IModbusClientService, ModbusClientService>();
        services.AddSingleton<IModbusServerService, ModbusServerService>();
        services.AddSingleton(CreateSharedRuntimeService);
        services.AddSingleton(CreateRouteMapTcpService);
        services.AddSingleton<IModbusDataMapRuntime>(sp =>
            (IModbusDataMapRuntime)sp.GetRequiredService<IModbusTcpService>());
        services.AddSingleton<IModbusDataSnapshotSource>(sp =>
            new ModbusDataSnapshotSourceAdapter(
                (IModbusDataSnapshotSource)sp.GetRequiredService<IModbusTcpService>()));
        services.AddSingleton<IModbusDemoOptionsProvider, ModbusDemoOptionsProvider>();
        services.AddSingleton(CreateDemoTcpService);

        return services;
    }

    private static IModbusRuntimeService CreateSharedRuntimeService(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var optionsMonitor = new NamedOptionsMonitor<ModbusOptions>(
            serviceProvider.GetRequiredService<IOptionsMonitor<ModbusOptions>>(),
            ModbusOptions.DemoSectionName);

        return new ModbusRuntimeService(
            serviceProvider.GetRequiredService<IModbusClientService>(),
            serviceProvider.GetRequiredService<IModbusServerService>(),
            optionsMonitor,
            loggerFactory.CreateLogger<ModbusRuntimeService>());
    }

    private static IModbusTcpService CreateRouteMapTcpService(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var optionsMonitor = new RouteMapModbusOptionsMonitor(
            serviceProvider.GetRequiredService<IOptionsMonitor<ModbusOptions>>());

        return new ModbusTcpService(
            serviceProvider.GetRequiredService<IModbusRuntimeService>(),
            serviceProvider.GetRequiredService<IModbusClientService>(),
            serviceProvider.GetRequiredService<IModbusServerService>(),
            optionsMonitor,
            serviceProvider.GetRequiredService<IModbusDataMapValidator>(),
            loggerFactory.CreateLogger<ModbusTcpService>());
    }

    private static IModbusDemoTcpService CreateDemoTcpService(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        // Демо-экран владеет lifecycle общего runtime, но использует собственную DataMap.
        var optionsMonitor = new NamedOptionsMonitor<ModbusOptions>(
            serviceProvider.GetRequiredService<IOptionsMonitor<ModbusOptions>>(),
            ModbusOptions.DemoSectionName);
        var facade = new ModbusTcpService(
            serviceProvider.GetRequiredService<IModbusRuntimeService>(),
            serviceProvider.GetRequiredService<IModbusClientService>(),
            serviceProvider.GetRequiredService<IModbusServerService>(),
            optionsMonitor,
            serviceProvider.GetRequiredService<IModbusDataMapValidator>(),
            loggerFactory.CreateLogger<ModbusTcpService>());

        return new ModbusDemoTcpService(facade);
    }
}
