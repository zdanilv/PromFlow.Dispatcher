namespace Configurator.Application.Services.Signals;

public interface IRouteMapRuntimeMapper<out TRuntimeState>
{
    TRuntimeState Map(IReadOnlyDictionary<string, SignalValue> signals);
}
