using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Signals;
using Configurator.Infrastructure.Modbus.RouteMap;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed class RouteMapModbusAdapterTests
{
    [Fact]
    public async Task Dispatcher_LatchedCommand_WritesRequestedValueOnce()
    {
        var service = new FakeModbusService();
        var options = CreateOptions(CreatePoint("command", ModbusWriteMode.Latched));
        var dispatcher = new ModbusTcpCommandDispatcher(service, new TestOptionsMonitor(options));

        await dispatcher.DispatchAsync(new SignalWriteRequest("command", true, SignalValueType.Bool));

        Assert.Equal([("command", true)], service.Writes);
    }

    [Fact]
    public async Task Dispatcher_PulseCommand_AlwaysResetsValue()
    {
        var service = new FakeModbusService();
        var point = CreatePoint("pulse", ModbusWriteMode.Pulse);
        point.PulseDurationMs = 1;
        var dispatcher = new ModbusTcpCommandDispatcher(
            service,
            new TestOptionsMonitor(CreateOptions(point)));

        await dispatcher.DispatchAsync(new SignalWriteRequest("pulse", true, SignalValueType.Bool));

        Assert.Equal([("pulse", true), ("pulse", false)], service.Writes);
    }

    [Fact]
    public async Task Dispatcher_RejectsUnknownReadOnlyAndWrongTypeSignals()
    {
        var service = new FakeModbusService();
        var readOnly = CreatePoint("readonly", ModbusWriteMode.Latched);
        readOnly.Access = ModbusDataAccess.Read;
        var dispatcher = new ModbusTcpCommandDispatcher(
            service,
            new TestOptionsMonitor(CreateOptions(readOnly)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchAsync(new SignalWriteRequest("missing", true, SignalValueType.Bool)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchAsync(new SignalWriteRequest("readonly", true, SignalValueType.Bool)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchAsync(new SignalWriteRequest("readonly", 1, SignalValueType.Int32)));
    }

    [Fact]
    public void SignalProvider_MapsValuesAndConnectionQualityWithoutThrowing()
    {
        var running = RunningState();
        var source = new FakeSnapshotSource();
        var service = new FakeModbusService(running);
        var point = CreatePoint("route.node.bsu_1.active", ModbusWriteMode.Latched);
        point.Access = ModbusDataAccess.Read;
        using var provider = new ModbusTcpSignalValueProvider(
            source,
            service,
            new TestOptionsMonitor(CreateOptions(point)),
            Options.Create(new RouteMapRuntimeOptions { StaleAfterMs = 250 }),
            NullLogger<ModbusTcpSignalValueProvider>.Instance);
        var observer = new RecordingObserver();
        using var subscription = provider.Observe().Subscribe(observer);

        source.Publish(new ModbusDataSnapshot(
            new Dictionary<string, ModbusDataValue>(StringComparer.OrdinalIgnoreCase)
            {
                [point.Name] = new(
                    point.Name,
                    point.Area,
                    point.Address,
                    point.Length,
                    point.Type,
                    true,
                    DateTimeOffset.Now)
            },
            DateTimeOffset.Now,
            running));

        var connected = observer.Latest;
        Assert.True(connected[point.Name].IsQualityGood);
        Assert.False(connected[point.Name].IsStale);
        Assert.Equal(true, connected[point.Name].Value);
        Assert.Equal(true, connected[RouteMapSystemSignalIds.ConnectionConnected].Value);

        service.PublishState(running with
        {
            ClientState = ModbusConnectionState.Reconnecting,
            IsWaitingForConnection = true,
            Message = "Reconnecting"
        });

        Assert.False(observer.Latest[point.Name].IsQualityGood);
        Assert.Equal("Reconnecting", observer.Latest[RouteMapSystemSignalIds.ConnectionStatus].Value);
        Assert.Equal(false, observer.Latest[RouteMapSystemSignalIds.ConnectionConnected].Value);

        service.PublishState(running);
        source.Publish(source.CurrentSnapshot with { Timestamp = DateTimeOffset.Now.AddSeconds(-1), State = running });
        Assert.True(observer.Latest[point.Name].IsStale);
        Assert.Equal(false, observer.Latest[RouteMapSystemSignalIds.ConnectionConnected].Value);
    }

    private static ModbusDataPointOptions CreatePoint(string name, ModbusWriteMode writeMode)
        => new()
        {
            Name = name,
            Area = ModbusDataArea.Coil,
            Address = 0,
            Length = 1,
            Access = ModbusDataAccess.ReadWrite,
            Type = ModbusValueType.Bool,
            WriteMode = writeMode
        };

    private static ModbusOptions CreateOptions(params ModbusDataPointOptions[] points)
        => new() { DataMap = points.ToList() };

    private static ModbusServiceState RunningState()
        => new(
            ModbusRunMode.Client,
            ModbusConnectionState.Running,
            ModbusConnectionState.Stopped,
            false,
            "Connected",
            null,
            DateTimeOffset.Now);

    private sealed class TestOptionsMonitor(ModbusOptions value) : IOptionsMonitor<ModbusOptions>
    {
        public ModbusOptions CurrentValue { get; } = value;
        public ModbusOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<ModbusOptions, string?> listener) => null;
    }

    private sealed class FakeSnapshotSource : IModbusDataSnapshotSource
    {
        public ModbusDataSnapshot CurrentSnapshot { get; private set; } = ModbusDataSnapshot.Empty;
        public event EventHandler<ModbusDataSnapshot>? SnapshotChanged;

        public void Publish(ModbusDataSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }
    }

    private sealed class FakeModbusService : IModbusTcpService
    {
        public FakeModbusService(ModbusServiceState? state = null)
        {
            State = state ?? ModbusServiceState.Stopped;
        }

        public List<(string SignalId, bool Value)> Writes { get; } = [];
        public ModbusServiceState State { get; private set; }
        public event EventHandler<ModbusServiceState>? StateChanged;

        public void PublishState(ModbusServiceState state)
        {
            State = state;
            StateChanged?.Invoke(this, state);
        }

        public Task<ModbusOperationResult> StartClientAsync(ModbusOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(ModbusOperationResult.Success());

        public Task<ModbusOperationResult> StartServerAsync(ModbusOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(ModbusOperationResult.Success());

        public Task<ModbusOperationResult> StopAsync(CancellationToken ct = default)
            => Task.FromResult(ModbusOperationResult.Success());

        public Task<ModbusOperationResult<T>> GetAsync<T>(string name, CancellationToken ct = default)
            => Task.FromResult(ModbusOperationResult<T>.Failure("Unavailable", "Unavailable"));

        public Task<ModbusOperationResult> SetAsync<T>(string name, T value, CancellationToken ct = default)
        {
            Writes.Add((name, Convert.ToBoolean(value)));
            return Task.FromResult(ModbusOperationResult.Success());
        }

        public IDisposable Subscribe(string name, Action<ModbusDataValue> onChanged)
            => new NoopDisposable();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingObserver : IObserver<IReadOnlyDictionary<string, SignalValue>>
    {
        public IReadOnlyDictionary<string, SignalValue> Latest { get; private set; }
            = new Dictionary<string, SignalValue>();

        public void OnCompleted() { }
        public void OnError(Exception error) => throw error;
        public void OnNext(IReadOnlyDictionary<string, SignalValue> value) => Latest = value;
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
