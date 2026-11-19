using System.Reactive.Linq;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Microsoft.Extensions.Options;

namespace Configurator.Desktop.Workspace.RouteMap.Services;

public sealed class RouteMapSignalRuntime : IRouteMapSignalRuntime
{
    private readonly ISignalValueProvider _mockProvider;
    private readonly IEquipmentCommandDispatcher _mockDispatcher;
    private readonly ISignalValueProvider _modbusProvider;
    private readonly IEquipmentCommandDispatcher _modbusDispatcher;
    private readonly RouteMapSessionJournal? _sessionJournal;
    private readonly RouteMapConfigurationManager? _configurationManager;
    private readonly IOptionsMonitor<ModbusOptions>? _modbusOptions;
    private readonly SignalSnapshotObservable _observable = new();
    private readonly object _sync = new();
    private IDisposable? _activeSubscription;
    private RouteMapSignalSource _currentSource;
    private long _generation;
    private bool _initialized;
    private bool _disposed;
    private IReadOnlyDictionary<string, SignalValue>? _lastSnapshot;

    // Save and reconnect share one attempt lane, including persistence of its outcome.
    internal SemaphoreSlim EquipmentParameterDispatchGate { get; } = new(1, 1);

    internal bool TryGetAvailableModbusGeneration(out long generation)
    {
        lock (_sync)
        {
            generation = _generation;
            return IsModbusAvailable();
        }
    }

    private bool IsModbusAvailable() => !_disposed && _currentSource == RouteMapSignalSource.Modbus
        && _lastSnapshot is not null
        && _lastSnapshot.TryGetValue(RouteMapSystemSignalIds.ConnectionConnected, out var connection)
        && connection.ValueType == SignalValueType.Bool && connection.Value is true
        && connection.IsQualityGood && !connection.IsStale;

    public RouteMapSignalRuntime(
        ISignalValueProvider mockProvider,
        IEquipmentCommandDispatcher mockDispatcher,
        ISignalValueProvider modbusProvider,
        IEquipmentCommandDispatcher modbusDispatcher,
        RouteMapSignalSource initialSource,
        RouteMapSessionJournal? sessionJournal = null,
        RouteMapConfigurationManager? configurationManager = null,
        IOptionsMonitor<ModbusOptions>? modbusOptions = null)
    {
        _mockProvider = mockProvider;
        _mockDispatcher = mockDispatcher;
        _modbusProvider = modbusProvider;
        _modbusDispatcher = modbusDispatcher;
        _sessionJournal = sessionJournal;
        _configurationManager = configurationManager;
        _modbusOptions = modbusOptions;
        SwitchSource(initialSource);
    }

    public RouteMapSignalSource CurrentSource
    {
        get
        {
            lock (_sync)
            {
                return _currentSource;
            }
        }
    }

    public event EventHandler<RouteMapSignalSourceChangedEventArgs>? SourceChanged;

    public IObservable<IReadOnlyDictionary<string, SignalValue>> Observe() => ObserveUpdates()
        .Where(update => !update.SourceChanged)
        .Select(update => update.Signals!);

    internal IObservable<SnapshotUpdate> ObserveUpdates() => Observable.Create<SnapshotUpdate>(observer =>
    {
        lock (_sync)
            return _observable.Subscribe(observer);
    });

    internal bool IsCurrentGeneration(long generation)
    {
        lock (_sync)
            return !_disposed && generation == _generation;
    }

    public void SwitchSource(RouteMapSignalSource source)
    {
        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        RouteMapSignalSource previousSource;
        long generation;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_initialized && _currentSource == source)
            {
                return;
            }

            previousSource = _currentSource;
            var sourceChanged = _initialized;
            _currentSource = source;
            _initialized = true;
            generation = ++_generation;
            _lastSnapshot = null;
            _activeSubscription?.Dispose();
            _activeSubscription = null;
            if (sourceChanged)
                _observable.Publish(new SnapshotUpdate(generation, null, SourceChanged: true));
        }

        var subscription = ProviderFor(source).Observe().Subscribe(
            snapshot => PublishIfCurrent(source, generation, snapshot));

        lock (_sync)
        {
            if (_disposed || generation != _generation)
            {
                subscription.Dispose();
            }
            else
            {
                _activeSubscription = subscription;
            }
        }

        SourceChanged?.Invoke(this, new RouteMapSignalSourceChangedEventArgs(previousSource, source));
    }

    public async Task DispatchAsync(
        SignalWriteRequest request,
        CancellationToken cancellationToken = default) =>
        await DispatchCoreAsync(request, expectedGeneration: null, cancellationToken);

    internal Task<bool> TryDispatchAtGenerationAsync(
        long generation, SignalWriteRequest request, CancellationToken cancellationToken) =>
        DispatchCoreAsync(request, generation, cancellationToken);

    internal Task<bool> TryDispatchEquipmentParameterAsync(
        long generation, SignalWriteRequest request, CancellationToken cancellationToken = default) =>
        DispatchCoreAsync(request, generation, cancellationToken, requireAvailableModbus: true);

    private async Task<bool> DispatchCoreAsync(
        SignalWriteRequest request, long? expectedGeneration, CancellationToken cancellationToken,
        bool requireAvailableModbus = false)
    {
        IEquipmentCommandDispatcher dispatcher;
        lock (_sync)
        {
            if (requireAvailableModbus && _disposed)
                return false;
            ObjectDisposedException.ThrowIf(_disposed, this);
            // Validate the snapshot source and select its dispatcher atomically.
            if (expectedGeneration is { } generation && generation != _generation)
                return false;
            if (requireAvailableModbus && !IsModbusAvailable())
                return false;
            if (requireAvailableModbus && cancellationToken.IsCancellationRequested)
                return false;
            dispatcher = DispatcherFor(_currentSource);
        }

        await dispatcher.DispatchAsync(request, cancellationToken);
        if (_sessionJournal is not null
            && _configurationManager is not null
            && _modbusOptions is not null)
        {
            _sessionJournal.RecordSignalSent(
                request,
                _configurationManager.CurrentDefinition,
                _modbusOptions.CurrentValue,
                DateTimeOffset.Now);
        }

        return true;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _generation++;
            _activeSubscription?.Dispose();
            _activeSubscription = null;
        }

        (_mockProvider as IDisposable)?.Dispose();
        if (!ReferenceEquals(_mockProvider, _mockDispatcher))
        {
            (_mockDispatcher as IDisposable)?.Dispose();
        }

        (_modbusProvider as IDisposable)?.Dispose();
        if (!ReferenceEquals(_modbusProvider, _modbusDispatcher))
        {
            (_modbusDispatcher as IDisposable)?.Dispose();
        }

        _observable.Dispose();
    }

    private ISignalValueProvider ProviderFor(RouteMapSignalSource source) =>
        source == RouteMapSignalSource.Mock ? _mockProvider : _modbusProvider;

    private IEquipmentCommandDispatcher DispatcherFor(RouteMapSignalSource source) =>
        source == RouteMapSignalSource.Mock ? _mockDispatcher : _modbusDispatcher;

    private void PublishIfCurrent(
        RouteMapSignalSource source,
        long generation,
        IReadOnlyDictionary<string, SignalValue> snapshot)
    {
        lock (_sync)
        {
            if (_disposed || generation != _generation || source != _currentSource)
            {
                return;
            }

            _lastSnapshot = snapshot;
            _observable.Publish(new SnapshotUpdate(generation, snapshot));
        }
    }

    internal sealed record SnapshotUpdate(long Generation, IReadOnlyDictionary<string, SignalValue>? Signals,
        bool SourceChanged = false);

    private sealed class SignalSnapshotObservable : IObservable<SnapshotUpdate>, IDisposable
    {
        private readonly object _sync = new();
        private readonly List<IObserver<SnapshotUpdate>> _observers = [];
        private SnapshotUpdate? _current;
        private bool _disposed;

        public IDisposable Subscribe(IObserver<SnapshotUpdate> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            SnapshotUpdate? current;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _observers.Add(observer);
                current = _current;
            }

            if (current is not null)
            {
                observer.OnNext(current);
            }

            return new Subscription(() => Unsubscribe(observer));
        }

        public void Publish(SnapshotUpdate snapshot)
        {
            IObserver<SnapshotUpdate>[] observers;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _current = snapshot;
                observers = _observers.ToArray();
            }

            foreach (var observer in observers)
            {
                try
                {
                    observer.OnNext(snapshot);
                }
                catch
                {
                    // A failed UI observer must not stop the active signal source.
                }
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _disposed = true;
                _observers.Clear();
                _current = null;
            }
        }

        private void Unsubscribe(IObserver<SnapshotUpdate> observer)
        {
            lock (_sync)
            {
                _observers.Remove(observer);
            }
        }

        private sealed class Subscription(Action dispose) : IDisposable
        {
            private Action? _dispose = dispose;

            public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }
}
