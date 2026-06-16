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
        services.AddSingleton<IModbusRuntimeService, ModbusRuntimeService>();
        services.AddSingleton<IModbusTcpService, ModbusTcpService>();
        services.AddSingleton<IModbusDataMapRuntime>(sp =>
            (IModbusDataMapRuntime)sp.GetRequiredService<IModbusTcpService>());
        services.AddSingleton<IModbusDataSnapshotSource>(sp =>
            new ModbusDataSnapshotSourceAdapter(
                (IModbusDataSnapshotSource)sp.GetRequiredService<IModbusTcpService>()));
        services.AddSingleton<IModbusDemoOptionsProvider, ModbusDemoOptionsProvider>();
        services.AddSingleton(CreateDemoTcpService);

        return services;
    }

    private static IModbusDemoTcpService CreateDemoTcpService(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        // Демо-экран не должен переиспользовать основной runtime: у него отдельная
        // секция настроек, клиент, сервер и facade, чтобы сценарии не мешали друг другу.
        var optionsMonitor = new NamedOptionsMonitor<ModbusOptions>(
            serviceProvider.GetRequiredService<IOptionsMonitor<ModbusOptions>>(),
            ModbusOptions.DemoSectionName);
        var client = new ModbusClientService(loggerFactory.CreateLogger<ModbusClientService>());
        var server = new ModbusServerService(loggerFactory.CreateLogger<ModbusServerService>());
        var runtime = new ModbusRuntimeService(
            client,
            server,
            optionsMonitor,
            loggerFactory.CreateLogger<ModbusRuntimeService>());
        var facade = new ModbusTcpService(
            runtime,
            client,
            server,
            optionsMonitor,
            serviceProvider.GetRequiredService<IModbusDataMapValidator>(),
            loggerFactory.CreateLogger<ModbusTcpService>());

        return new ModbusDemoTcpService(facade, runtime, client, server);
    }
}
