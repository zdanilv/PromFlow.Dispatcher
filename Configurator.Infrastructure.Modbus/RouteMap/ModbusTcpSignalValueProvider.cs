using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Signals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Configurator.Infrastructure.Modbus.RouteMap;

public sealed class ModbusTcpSignalValueProvider : ISignalValueProvider, IDisposable
{
    private readonly IModbusDataSnapshotSource _snapshotSource;
    private readonly IModbusTcpService _modbusService;
    private readonly IOptionsMonitor<ModbusOptions> _modbusOptions;
    private readonly ILogger<ModbusTcpSignalValueProvider> _logger;
    private readonly SignalSnapshotObservable _observable;
    private readonly Timer _staleTimer;
    private readonly int _staleAfterMs;
    private readonly object _sync = new();
    private ModbusDataSnapshot _latestSnapshot;
    private ModbusServiceState _latestState;
    private DateTimeOffset _snapshotNotBefore;
    private long _lastPublicationSequence;
    private bool _disposed;

    public ModbusTcpSignalValueProvider(
        IModbusDataSnapshotSource snapshotSource,
        IModbusTcpService modbusService,
        IOptionsMonitor<ModbusOptions> modbusOptions,
        IOptions<RouteMapRuntimeOptions> runtimeOptions,
        ILogger<ModbusTcpSignalValueProvider> logger)
    {
        _snapshotSource = snapshotSource;
        _modbusService = modbusService;
        _modbusOptions = modbusOptions;
        _logger = logger;
        _observable = new SignalSnapshotObservable(_sync);
        _staleAfterMs = Math.Clamp(runtimeOptions.Value.StaleAfterMs, 250, 60000);

        lock (_sync)
        {
            _snapshotSource.SnapshotChanged += OnSnapshotChanged;
            _modbusService.StateChanged += OnStateChanged;
            _latestState = modbusService.State;
            _snapshotNotBefore = _latestState.UpdatedAt;
            var cached = snapshotSource.CurrentSnapshot;
            _lastPublicationSequence = cached.PublicationSequence;
            _latestSnapshot = IsRuntimeRunning(_latestState) && cached.Timestamp > _snapshotNotBefore
                ? cached
                : ModbusDataSnapshot.Empty;
        }
        _staleTimer = new Timer(
            _ => PublishCurrent(),
            null,
            TimeSpan.FromMilliseconds(Math.Max(250, _staleAfterMs / 2)),
            TimeSpan.FromMilliseconds(Math.Max(250, _staleAfterMs / 2)));

        PublishCurrent();
    }

    public IObservable<IReadOnlyDictionary<string, SignalValue>> Observe() => _observable;

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        _snapshotSource.SnapshotChanged -= OnSnapshotChanged;
        _modbusService.StateChanged -= OnStateChanged;
        _staleTimer.Dispose();
    }

    private void OnSnapshotChanged(object? sender, ModbusDataSnapshot snapshot)
    {
        lock (_sync)
        {
            if (_disposed || !IsRuntimeRunning(_latestState) || !IsRuntimeRunning(snapshot.State))
                return;

            // A notification can be delayed in another subscriber after its commit.
            // Keep the floor across empty resets and connection invalidation.
            _lastPublicationSequence = Math.Max(_lastPublicationSequence, _snapshotSource.CurrentSnapshot.PublicationSequence);
            if (snapshot.PublicationSequence < _lastPublicationSequence)
                return;
            _lastPublicationSequence = Math.Max(_lastPublicationSequence, snapshot.PublicationSequence);

            // A data-map reset publishes an empty snapshot with the sentinel timestamp.
            if (snapshot.Timestamp != DateTimeOffset.MinValue &&
                (snapshot.Timestamp <= _snapshotNotBefore || snapshot.Timestamp < _latestSnapshot.Timestamp ||
                    snapshot.Timestamp > DateTimeOffset.Now))
                return;

            _latestSnapshot = snapshot;
        }

        PublishCurrent();
    }

    private void OnStateChanged(object? sender, ModbusServiceState state)
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            var wasRunning = IsRuntimeRunning(_latestState);
            var previousState = _latestState;
            _latestState = _modbusService.State;
            _lastPublicationSequence = Math.Max(_lastPublicationSequence, _snapshotSource.CurrentSnapshot.PublicationSequence);
            // Clear on every loss of connection, even if the clock moved back or Running already followed it.
            if (!wasRunning || !IsRuntimeRunning(state) || !IsRuntimeRunning(_latestState)
                || previousState.ActiveRole != _latestState.ActiveRole
                || (previousState.ClientState == ModbusConnectionState.Running && state.ClientState != ModbusConnectionState.Running)
                || (previousState.ServerState == ModbusConnectionState.Running && state.ServerState != ModbusConnectionState.Running))
            {
                _snapshotNotBefore = _latestState.UpdatedAt;
                _latestSnapshot = ModbusDataSnapshot.Empty;
            }
        }

        PublishCurrent();
    }

    private void PublishCurrent()
    {
        var points = _modbusOptions.CurrentValue.DataMap;
        lock (_sync)
        {
            if (!_disposed)
                PublishSnapshot(points, _latestSnapshot, _latestState);
        }
    }

    private void PublishSnapshot(IEnumerable<ModbusDataPointOptions> points, ModbusDataSnapshot snapshot, ModbusServiceState state)
    {
        var now = DateTimeOffset.Now;
        var isRuntimeRunning = IsRuntimeRunning(state);
        var isStale = snapshot.Timestamp == DateTimeOffset.MinValue
            || snapshot.Timestamp > now
            || now - snapshot.Timestamp > TimeSpan.FromMilliseconds(_staleAfterMs);
        var isConnected = isRuntimeRunning && !isStale;
        var signals = new Dictionary<string, SignalValue>(StringComparer.OrdinalIgnoreCase);

        foreach (var point in points.Where(point => point.IsReadable))
        {
            if (!TryMapType(point.Type, out var signalType))
            {
                _logger.LogDebug(
                    "Skipping Modbus point {SignalId}: value type {ValueType} is unsupported by RouteMap",
                    point.Name,
                    point.Type);
                continue;
            }

            snapshot.Values.TryGetValue(point.Name, out var dataValue);
            var readState = point.Area == ModbusDataArea.Coil ? snapshot.CoilsReadState : snapshot.HoldingRegistersReadState;
            var signalTimestamp = readState?.LastSuccessfulReadAt ?? dataValue?.Timestamp ?? snapshot.Timestamp;
            var signalIsStale = signalTimestamp == DateTimeOffset.MinValue || signalTimestamp > now
                || now - signalTimestamp > TimeSpan.FromMilliseconds(_staleAfterMs);
            signals[point.Name] = new SignalValue(
                point.Name,
                dataValue?.Value,
                signalType,
                signalTimestamp,
                IsQualityGood: isRuntimeRunning && dataValue is not null && (readState?.IsGood ?? true),
                IsStale: signalIsStale || dataValue is null || (readState is { IsGood: false }));
        }

        signals[RouteMapSystemSignalIds.ConnectionStatus] = new SignalValue(
            RouteMapSystemSignalIds.ConnectionStatus,
            state.Message,
            SignalValueType.String,
            state.UpdatedAt,
            IsQualityGood: true,
            IsStale: false);
        signals[RouteMapSystemSignalIds.ConnectionConnected] = new SignalValue(
            RouteMapSystemSignalIds.ConnectionConnected,
            isConnected,
            SignalValueType.Bool,
            now,
            IsQualityGood: true,
            IsStale: false);

        _observable.Publish(signals);
    }

    private static bool IsRuntimeRunning(ModbusServiceState state) =>
        state.ClientState == ModbusConnectionState.Running || state.ServerState == ModbusConnectionState.Running;

    private static bool TryMapType(ModbusValueType type, out SignalValueType signalType) =>
        SignalModbusTypeCompatibility.TryMapFromModbus(type, out signalType);

    private sealed class SignalSnapshotObservable(object deliverySync) : IObservable<IReadOnlyDictionary<string, SignalValue>>
    {
        private readonly object _sync = new();
        private readonly List<IObserver<IReadOnlyDictionary<string, SignalValue>>> _observers = [];
        private IReadOnlyDictionary<string, SignalValue>? _current;

        public IDisposable Subscribe(IObserver<IReadOnlyDictionary<string, SignalValue>> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            // Replay and live delivery share a lock. Membership remains separate so an
            // observer can unsubscribe while another observer is receiving a snapshot.
            lock (deliverySync)
            {
                IReadOnlyDictionary<string, SignalValue>? current;
                lock (_sync)
                {
                    _observers.Add(observer);
                    current = _current;
                }

                if (current is not null)
                    observer.OnNext(current);
            }

            return new Subscription(() => Unsubscribe(observer));
        }

        public void Publish(IReadOnlyDictionary<string, SignalValue> snapshot)
        {
            IObserver<IReadOnlyDictionary<string, SignalValue>>[] observers;
            lock (_sync)
            {
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
                    // A failed consumer must not stop the Modbus poll/timer thread.
                }
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
