using Configurator.Application.Services.Modbus.Configuration;
using Microsoft.Extensions.Options;

namespace Configurator.Infrastructure.Modbus.Configuration;

internal sealed class RouteMapModbusOptionsMonitor(
    IOptionsMonitor<ModbusOptions> inner) : IOptionsMonitor<ModbusOptions>
{
    public ModbusOptions CurrentValue => Compose();

    public ModbusOptions Get(string? name)
    {
        return string.Equals(name, ModbusOptions.DemoSectionName, StringComparison.Ordinal)
            ? inner.Get(ModbusOptions.DemoSectionName).Clone()
            : Compose();
    }

    public IDisposable? OnChange(Action<ModbusOptions, string?> listener)
    {
        return inner.OnChange((_, changedName) =>
        {
            if (string.IsNullOrEmpty(changedName)
                || string.Equals(changedName, Options.DefaultName, StringComparison.Ordinal)
                || string.Equals(changedName, ModbusOptions.DemoSectionName, StringComparison.Ordinal))
            {
                listener(Compose(), changedName);
            }
        });
    }

    private ModbusOptions Compose()
    {
        var routeMap = inner.CurrentValue.Clone();
        var demo = inner.Get(ModbusOptions.DemoSectionName).Clone();

        routeMap.AutostartOnWorkspaceOpen = demo.AutostartOnWorkspaceOpen;
        routeMap.StartupMode = demo.StartupMode;
        routeMap.Client = demo.Client.Clone();
        routeMap.Server = demo.Server.Clone();

        return routeMap;
    }
}
