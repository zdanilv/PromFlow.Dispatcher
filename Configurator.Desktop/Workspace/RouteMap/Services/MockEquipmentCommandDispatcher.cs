using Configurator.Application.Services.Signals;

namespace Configurator.Desktop.Workspace.RouteMap.Services;

public sealed class MockEquipmentCommandDispatcher(MockSignalState state) : IEquipmentCommandDispatcher
{
    public Task DispatchAsync(SignalWriteRequest request, CancellationToken cancellationToken = default)
    {
        state.Apply(request);
        return Task.CompletedTask;
    }
}
