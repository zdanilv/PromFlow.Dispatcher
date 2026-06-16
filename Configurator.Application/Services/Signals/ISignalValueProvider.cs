namespace Configurator.Application.Services.Signals;

public interface ISignalValueProvider
{
    IObservable<IReadOnlyDictionary<string, SignalValue>> Observe();
}
