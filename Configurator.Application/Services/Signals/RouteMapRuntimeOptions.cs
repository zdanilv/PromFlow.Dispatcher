namespace Configurator.Application.Services.Signals;

public enum RouteMapSignalSource
{
    Mock,
    Modbus
}

public sealed class RouteMapRuntimeOptions
{
    public const string SectionName = "RouteMapRuntime";

    public RouteMapSignalSource SignalSource { get; set; } = RouteMapSignalSource.Mock;

    public int StaleAfterMs { get; set; } = 1500;
}
