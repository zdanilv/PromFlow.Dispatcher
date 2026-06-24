using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Licensing;
using Configurator.Application.Services.Signals;

namespace Configurator.Desktop.Workspace.RouteMap.Services;

public sealed class RouteMapSignalRuntime : IRouteMapSignalRuntime
{
    private readonly ISignalValueProvider _mockProvider;
    private readonly IEquipmentCommandDispatcher _mockDispatcher;
    private readonly ISignalValueProvider _modbusProvider;
    private readonly IEquipmentCommandDispatcher _modbusDispatcher;
    private readonly IAccessDecisionService _accessDecisionService;
    private readonly SignalSnapshotObservable _observable = new();
    private readonly object _sync = new();
    private IDisposable? _activeSubscription;
    private RouteMapSignalSource _currentSource;
    private long _generation;
    private bool _initialized;
    private bool _disposed;

    public RouteMapSignalRuntime(
        ISignalValueProvider mockProvider,
        IEquipmentCommandDispatcher mockDispatcher,
        ISignalValueProvider modbusProvider,
        IEquipmentCommandDispatcher modbusDispatcher,
        IAccessDecisionService accessDecisionService,
        RouteMapSignalSource initialSource)
    {
        _mockProvider = mockProvider;
        _mockDispatcher = mockDispatcher;
        _modbusProvider = modbusProvider;
        _modbusDispatcher = modbusDispatcher;
        _accessDecisionService = accessDecisionService ?? throw new ArgumentNullException(nameof(accessDecisionService));
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

    public IObservable<IReadOnlyDictionary<string, SignalValue>> Observe() => _observable;

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
            _currentSource = source;
            _initialized = true;
            generation = ++_generation;
            _activeSubscription?.Dispose();
            _activeSubscription = null;
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

    public Task DispatchAsync(
        SignalWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IEquipmentCommandDispatcher dispatcher;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            dispatcher = DispatcherFor(_currentSource);
        }

        var decision = _accessDecisionService.Authorize(
            new AccessRequirement(Permission.IssueEquipmentCommands, LicenseFeature.RemoteControl));
        if (!decision.Succeeded)
        {
            throw new UnauthorizedAccessException(
                $"Issue equipment commands permission is required. Reason: {decision.ReasonCode}");
        }

        return dispatcher.DispatchAsync(request, cancellationToken);
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
        }

        _observable.Publish(snapshot);
    }

    private sealed class SignalSnapshotObservable : IObservable<IReadOnlyDictionary<string, SignalValue>>, IDisposable
    {
        private readonly object _sync = new();
        private readonly List<IObserver<IReadOnlyDictionary<string, SignalValue>>> _observers = [];
        private IReadOnlyDictionary<string, SignalValue>? _current;
        private bool _disposed;

        public IDisposable Subscribe(IObserver<IReadOnlyDictionary<string, SignalValue>> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            IReadOnlyDictionary<string, SignalValue>? current;
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

        public void Publish(IReadOnlyDictionary<string, SignalValue> snapshot)
        {
            IObserver<IReadOnlyDictionary<string, SignalValue>>[] observers;
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

        private void Unsubscribe(IObserver<IReadOnlyDictionary<string, SignalValue>> observer)
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
