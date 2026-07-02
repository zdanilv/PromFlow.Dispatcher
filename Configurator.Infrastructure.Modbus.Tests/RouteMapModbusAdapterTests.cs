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
    public async Task Dispatcher_AllowsWordDwordAndDateWrites()
    {
        var service = new FakeModbusService();
        var options = CreateOptions(
            CreateRegisterPoint("word", ModbusValueType.Word, length: 1),
            CreateRegisterPoint("legacy.word", ModbusValueType.UInt16, length: 1),
            CreateRegisterPoint("dword", ModbusValueType.Dword, length: 2),
            CreateRegisterPoint("date", ModbusValueType.Date, length: 2));
        var dispatcher = new ModbusTcpCommandDispatcher(service, new TestOptionsMonitor(options));
        var date = new DateTime(2026, 6, 30, 12, 34, 56, DateTimeKind.Local);

        await dispatcher.DispatchAsync(new SignalWriteRequest("word", (ushort)12, SignalValueType.Word));
        await dispatcher.DispatchAsync(new SignalWriteRequest("legacy.word", (ushort)13, SignalValueType.Word));
        await dispatcher.DispatchAsync(new SignalWriteRequest("dword", 123456u, SignalValueType.Dword));
        await dispatcher.DispatchAsync(new SignalWriteRequest("date", date, SignalValueType.Date));

        Assert.Collection(service.RawWrites,
            write =>
            {
                Assert.Equal("word", write.SignalId);
                Assert.Equal((ushort)12, Assert.IsType<ushort>(write.Value));
            },
            write =>
            {
                Assert.Equal("legacy.word", write.SignalId);
                Assert.Equal((ushort)13, Assert.IsType<ushort>(write.Value));
            },
            write =>
            {
                Assert.Equal("dword", write.SignalId);
                Assert.Equal(123456u, Assert.IsType<uint>(write.Value));
            },
            write =>
            {
                Assert.Equal("date", write.SignalId);
                Assert.Equal(date, Assert.IsType<DateTime>(write.Value));
            });
    }

    [Fact]
    public void SignalProvider_MapsValuesAndConnectionQualityWithoutThrowing()
    {
        var running = RunningState();
        var source = new FakeSnapshotSource();
        var service = new FakeModbusService(running);
        var point = CreatePoint("route.node.bsu_1.active", ModbusWriteMode.Latched);
        point.Access = ModbusDataAccess.Read;
        var wordPoint = CreateRegisterPoint("route.word", ModbusValueType.Word, length: 1);
        wordPoint.Access = ModbusDataAccess.Read;
        var dwordPoint = CreateRegisterPoint("route.dword", ModbusValueType.Dword, length: 2);
        dwordPoint.Access = ModbusDataAccess.Read;
        var datePoint = CreateRegisterPoint("route.date", ModbusValueType.Date, length: 2);
        datePoint.Access = ModbusDataAccess.Read;
        using var provider = new ModbusTcpSignalValueProvider(
            source,
            service,
            new TestOptionsMonitor(CreateOptions(point, wordPoint, dwordPoint, datePoint)),
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
                    DateTimeOffset.Now),
                [wordPoint.Name] = new(
                    wordPoint.Name,
                    wordPoint.Area,
                    wordPoint.Address,
                    wordPoint.Length,
                    wordPoint.Type,
                    (ushort)42,
                    DateTimeOffset.Now),
                [dwordPoint.Name] = new(
                    dwordPoint.Name,
                    dwordPoint.Area,
                    dwordPoint.Address,
                    dwordPoint.Length,
                    dwordPoint.Type,
                    123456u,
                    DateTimeOffset.Now),
                [datePoint.Name] = new(
                    datePoint.Name,
                    datePoint.Area,
                    datePoint.Address,
                    datePoint.Length,
                    datePoint.Type,
                    new DateTime(2026, 6, 30, 12, 34, 56, DateTimeKind.Utc),
                    DateTimeOffset.Now)
            },
            DateTimeOffset.Now,
            running));

        var connected = observer.Latest;
        Assert.True(connected[point.Name].IsQualityGood);
        Assert.False(connected[point.Name].IsStale);
        Assert.Equal(true, connected[point.Name].Value);
        Assert.Equal(SignalValueType.Word, connected[wordPoint.Name].ValueType);
        Assert.Equal((ushort)42, connected[wordPoint.Name].Value);
        Assert.Equal(SignalValueType.Dword, connected[dwordPoint.Name].ValueType);
        Assert.Equal(123456u, connected[dwordPoint.Name].Value);
        Assert.Equal(SignalValueType.Date, connected[datePoint.Name].ValueType);
        Assert.IsType<DateTime>(connected[datePoint.Name].Value);
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

    private static ModbusDataPointOptions CreateRegisterPoint(
        string name,
        ModbusValueType type,
        int length)
        => new()
        {
            Name = name,
            Area = ModbusDataArea.HoldingRegister,
            Address = 0,
            Length = length,
            Access = ModbusDataAccess.ReadWrite,
            Type = type,
            WriteMode = ModbusWriteMode.Latched
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
        public List<(string SignalId, object? Value)> RawWrites { get; } = [];
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
            RawWrites.Add((name, value));
            if (value is bool boolean)
            {
                Writes.Add((name, boolean));
            }

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
