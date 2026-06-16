namespace Configurator.Application.Services.Signals;

public sealed class RouteMapSignalSourceChangedEventArgs(
    RouteMapSignalSource previousSource,
    RouteMapSignalSource currentSource) : EventArgs
{
    public RouteMapSignalSource PreviousSource { get; } = previousSource;
    public RouteMapSignalSource CurrentSource { get; } = currentSource;
}

public interface IRouteMapSignalRuntime : ISignalValueProvider, IEquipmentCommandDispatcher, IDisposable
{
    RouteMapSignalSource CurrentSource { get; }

    event EventHandler<RouteMapSignalSourceChangedEventArgs>? SourceChanged;

    void SwitchSource(RouteMapSignalSource source);
}
