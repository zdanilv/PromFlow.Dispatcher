using System.Collections.Concurrent;
using Configurator.Application.Services.Signals;

namespace Configurator.Desktop.Workspace.RouteMap.Services;

public sealed class MockSignalState
{
    private readonly ConcurrentDictionary<string, SignalWriteRequest> _writes = new(StringComparer.Ordinal);

    public void Apply(SignalWriteRequest request) => _writes[request.SignalId] = request;

    public bool TryGet(string signalId, out SignalWriteRequest request) =>
        _writes.TryGetValue(signalId, out request!);
}
