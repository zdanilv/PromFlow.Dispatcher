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
    private readonly SignalSnapshotObservable _observable = new();
    private readonly Timer _staleTimer;
    private readonly int _staleAfterMs;
    private readonly object _sync = new();
    private ModbusDataSnapshot _latestSnapshot;
    private ModbusServiceState _latestState;
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
        _latestSnapshot = snapshotSource.CurrentSnapshot;
        _latestState = modbusService.State;
        _staleAfterMs = Math.Clamp(runtimeOptions.Value.StaleAfterMs, 250, 60000);

        _snapshotSource.SnapshotChanged += OnSnapshotChanged;
        _modbusService.StateChanged += OnStateChanged;
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _snapshotSource.SnapshotChanged -= OnSnapshotChanged;
        _modbusService.StateChanged -= OnStateChanged;
        _staleTimer.Dispose();
    }

    private void OnSnapshotChanged(object? sender, ModbusDataSnapshot snapshot)
    {
        lock (_sync)
        {
            _latestSnapshot = snapshot;
            _latestState = snapshot.State;
        }

        PublishCurrent();
    }

    private void OnStateChanged(object? sender, ModbusServiceState state)
    {
        lock (_sync)
        {
            _latestState = state;
        }

        PublishCurrent();
    }

    private void PublishCurrent()
    {
        if (_disposed)
        {
            return;
        }

        ModbusDataSnapshot snapshot;
        ModbusServiceState state;
        lock (_sync)
        {
            snapshot = _latestSnapshot;
            state = _latestState;
        }

        var now = DateTimeOffset.Now;
        var isRuntimeRunning = state.ClientState == ModbusConnectionState.Running
            || state.ServerState == ModbusConnectionState.Running;
        var isStale = snapshot.Timestamp == DateTimeOffset.MinValue
            || now - snapshot.Timestamp > TimeSpan.FromMilliseconds(_staleAfterMs);
        var isConnected = isRuntimeRunning && !isStale;
        var signals = new Dictionary<string, SignalValue>(StringComparer.OrdinalIgnoreCase);

        foreach (var point in _modbusOptions.CurrentValue.DataMap.Where(point => point.IsReadable))
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
            signals[point.Name] = new SignalValue(
                point.Name,
                dataValue?.Value,
                signalType,
                dataValue?.Timestamp ?? snapshot.Timestamp,
                IsQualityGood: isRuntimeRunning && dataValue is not null,
                IsStale: isStale || dataValue is null);
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

    private static bool TryMapType(ModbusValueType type, out SignalValueType signalType)
    {
        signalType = type switch
        {
            ModbusValueType.Bool => SignalValueType.Bool,
            ModbusValueType.UInt16 => SignalValueType.UInt16,
            ModbusValueType.Int => SignalValueType.Int32,
            ModbusValueType.Real => SignalValueType.Float32,
            ModbusValueType.String => SignalValueType.String,
            _ => default
        };

        return type is ModbusValueType.Bool
            or ModbusValueType.UInt16
            or ModbusValueType.Int
            or ModbusValueType.Real
            or ModbusValueType.String;
    }

    private sealed class SignalSnapshotObservable : IObservable<IReadOnlyDictionary<string, SignalValue>>
    {
        private readonly object _sync = new();
        private readonly List<IObserver<IReadOnlyDictionary<string, SignalValue>>> _observers = [];
        private IReadOnlyDictionary<string, SignalValue>? _current;

        public IDisposable Subscribe(IObserver<IReadOnlyDictionary<string, SignalValue>> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            IReadOnlyDictionary<string, SignalValue>? current;
            lock (_sync)
            {
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
