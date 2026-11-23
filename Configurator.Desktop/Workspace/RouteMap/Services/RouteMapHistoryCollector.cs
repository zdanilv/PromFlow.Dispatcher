using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Microsoft.Extensions.Options;

namespace Configurator.Desktop.Workspace.RouteMap.Services;

public sealed class RouteMapHistoryCollector(
    RouteMapSignalRuntime runtime,
    RouteMapSessionJournal journal,
    RouteMapConfigurationManager configuration,
    IOptionsMonitor<ModbusOptions> modbusOptions) : IDisposable
{
    private readonly object _lifecycleSync = new();
    private readonly object _collectSync = new();
    private IDisposable? _subscription;
    private bool _disposed;
    private long? _generation;

    public void Start()
    {
        lock (_lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _subscription ??= runtime.ObserveUpdates().Subscribe(Collect);
        }
    }

    public void Dispose()
    {
        lock (_lifecycleSync)
        {
            // Finish any callback already in progress before the writer closes its queue.
            lock (_collectSync)
                _disposed = true;
            _subscription?.Dispose();
            _subscription = null;
        }
    }

    private void Collect(RouteMapSignalRuntime.SnapshotUpdate update)
    {
        // ObserveUpdates delivers source changes and snapshots in generation order under the runtime lock.
        lock (_collectSync)
        {
            if (_disposed)
                return;

            if (_generation != update.Generation)
            {
                _generation = update.Generation;
                journal.ResetSignalSnapshot();
            }

            if (update.Signals is { } signals)
                journal.RecordSignalSnapshot(signals, configuration.CurrentDefinition, modbusOptions.CurrentValue, update.Source);
        }
    }
}
