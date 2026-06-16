namespace Configurator.Application.Services.Signals;

public interface IEquipmentCommandDispatcher
{
    Task DispatchAsync(SignalWriteRequest request, CancellationToken cancellationToken = default);
}
