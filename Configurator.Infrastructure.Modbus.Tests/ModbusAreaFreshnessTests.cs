using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Application.Services.Signals;
using Configurator.Infrastructure.Modbus.Client;
using Configurator.Infrastructure.Modbus.RouteMap;
using Configurator.Infrastructure.Modbus.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;
using Xunit;
using System.Buffers.Binary;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed class ModbusAreaFreshnessTests
{
    [Fact]
    public async Task Client_TracksSuccessfulUnchangedCyclesIndependentlyForBothAreas()
    {
        var port = GetAvailablePort();
        await using var server = new ModbusServerService(NullLogger<ModbusServerService>.Instance);
        await using var client = new ModbusClientService(NullLogger<ModbusClientService>.Instance);
        await server.StartAsync(Endpoint(port, coils: 2, registers: 2));
        await server.SetCoilAsync(0, true);
        await server.SetRegisterAsync(0, 37);
        await client.StartAsync(Endpoint(port, coils: 2, registers: 2));

        Assert.True(await WaitForAsync(() =>
            client.Snapshot.CoilsReadState?.IsGood == true
            && client.Snapshot.HoldingRegistersReadState?.IsGood == true
            && client.Snapshot.CoilsReadState.SuccessfulReadSequence >= 1
            && client.Snapshot.HoldingRegistersReadState.SuccessfulReadSequence >= 1));

        var first = client.Snapshot;
        Assert.NotEqual(Guid.Empty, first.SourceSessionId);
        Assert.NotEqual(first.CoilsReadState!.SuccessfulReadSequence, first.HoldingRegistersReadState!.SuccessfulReadSequence);
        Assert.True(first.Coils[0]);
        Assert.Equal((ushort)37, first.HoldingRegisters[0]);

        Assert.True(await WaitForAsync(() =>
            client.Snapshot.CoilsReadState!.SuccessfulReadSequence > first.CoilsReadState.SuccessfulReadSequence
            && client.Snapshot.HoldingRegistersReadState!.SuccessfulReadSequence > first.HoldingRegistersReadState.SuccessfulReadSequence));

        var second = client.Snapshot;
        Assert.True(second.Coils[0]);
        Assert.Equal((ushort)37, second.HoldingRegisters[0]);
        Assert.True(second.CoilsReadState!.LastSuccessfulReadAt >= first.CoilsReadState.LastSuccessfulReadAt);
        Assert.True(second.HoldingRegistersReadState!.LastSuccessfulReadAt >= first.HoldingRegistersReadState.LastSuccessfulReadAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Client_PartialAreaFailurePreservesLastSuccessAndRecoveryNeedsNewRead(bool failCoils)
    {
        await using var peer = new ControlledModbusPeer();
        var options = CreateFacadeOptions(peer.Port);
        await using var client = new ModbusClientService(NullLogger<ModbusClientService>.Instance);
        await using var server = new ModbusServerService(NullLogger<ModbusServerService>.Instance);
        var monitor = new TestOptionsMonitor(options);
        await using var runtime = new ModbusRuntimeService(client, server, monitor, NullLogger<ModbusRuntimeService>.Instance);
        await using var facade = new ModbusTcpService(runtime, client, server, monitor,
            new ModbusDataMapValidator(), NullLogger<ModbusTcpService>.Instance);
        using var provider = new ModbusTcpSignalValueProvider(facade, facade, monitor,
            Options.Create(new RouteMapRuntimeOptions { StaleAfterMs = 60000 }),
            NullLogger<ModbusTcpSignalValueProvider>.Instance);
        var observer = new RecordingSignalObserver();
        using var signalSubscription = provider.Observe().Subscribe(observer);

        Assert.True((await facade.StartClientAsync(options)).Succeeded);
        Assert.True(await WaitForAsync(() => facade.CurrentSnapshot.Values.Count == 2
            && facade.CurrentSnapshot.CoilsReadState is { IsGood: true }
            && facade.CurrentSnapshot.HoldingRegistersReadState is { IsGood: true }));

        var successfulSnapshot = facade.CurrentSnapshot;
        var sourceSessionId = successfulSnapshot.SourceSessionId;
        var priorFailedReadCount = failCoils ? peer.FailedCoilReads : peer.FailedRegisterReads;
        peer.FailFunctionCode = failCoils ? 1 : 3;
        Assert.True(await WaitForAsync(() =>
        {
            var snapshot = facade.CurrentSnapshot;
            var failedReadCount = failCoils ? peer.FailedCoilReads : peer.FailedRegisterReads;
            var failedState = failCoils ? snapshot.CoilsReadState : snapshot.HoldingRegistersReadState;
            var goodState = failCoils ? snapshot.HoldingRegistersReadState : snapshot.CoilsReadState;
            var failedSignal = observer.Latest.GetValueOrDefault(failCoils ? "coil" : "reg");
            var goodSignal = observer.Latest.GetValueOrDefault(failCoils ? "reg" : "coil");
            return failedReadCount >= priorFailedReadCount + 3
                && failedState is { IsGood: false, HasSuccessfulRead: true, LastAttemptSucceeded: false }
                && goodState is { IsGood: true }
                && failedSignal is { IsQualityGood: false, IsStale: true }
                && goodSignal is { IsQualityGood: true, IsStale: false }
                && snapshot.Values.Count == 1;
        }));

        var failedSnapshot = facade.CurrentSnapshot;
        var failedArea = failCoils ? failedSnapshot.CoilsReadState! : failedSnapshot.HoldingRegistersReadState!;
        var goodArea = failCoils ? failedSnapshot.HoldingRegistersReadState! : failedSnapshot.CoilsReadState!;
        Assert.True(failedArea.HasSuccessfulRead);
        Assert.False(failedArea.LastAttemptSucceeded);
        Assert.True(goodArea.SuccessfulReadSequence > 0);
        Assert.Equal(sourceSessionId, failedSnapshot.SourceSessionId);
        Assert.Equal(ModbusConnectionState.Running, facade.State.ClientState);
        var firstFailedSequence = failedArea.SuccessfulReadSequence;
        var firstFailedAt = failedArea.LastSuccessfulReadAt;

        var failedReadsAfterFirstFailure = failCoils ? peer.FailedCoilReads : peer.FailedRegisterReads;
        Assert.True(await WaitForAsync(() => (failCoils ? peer.FailedCoilReads : peer.FailedRegisterReads)
            >= failedReadsAfterFirstFailure + 3));
        failedSnapshot = facade.CurrentSnapshot;
        failedArea = failCoils ? failedSnapshot.CoilsReadState! : failedSnapshot.HoldingRegistersReadState!;
        Assert.Equal(firstFailedSequence, failedArea.SuccessfulReadSequence);
        Assert.Equal(firstFailedAt, failedArea.LastSuccessfulReadAt);

        peer.FailFunctionCode = 0;
        Assert.True(await WaitForAsync(() =>
            facade.CurrentSnapshot.CoilsReadState is { IsGood: true }
            && facade.CurrentSnapshot.HoldingRegistersReadState is { IsGood: true }
            && (failCoils
                ? facade.CurrentSnapshot.CoilsReadState.SuccessfulReadSequence > firstFailedSequence
                : facade.CurrentSnapshot.HoldingRegistersReadState.SuccessfulReadSequence > firstFailedSequence)
            && facade.CurrentSnapshot.Values.Count == 2));
        Assert.Equal(sourceSessionId, facade.CurrentSnapshot.SourceSessionId);
        Assert.True(observer.Latest[failCoils ? "coil" : "reg"].IsQualityGood);
        Assert.True(observer.Latest[failCoils ? "reg" : "coil"].IsQualityGood);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Client_FirstFailedAreaAttemptHasNoSuccessfulRead(bool failCoils)
    {
        await using var peer = new ControlledModbusPeer { FailFunctionCode = failCoils ? 1 : 3 };
        await using var client = new ModbusClientService(NullLogger<ModbusClientService>.Instance);
        await client.StartAsync(Endpoint(peer.Port, coils: 1, registers: 1));

        Assert.True(await WaitForAsync(() => failCoils
            ? client.Snapshot.CoilsReadState is { Enabled: true, HasSuccessfulRead: false, LastAttemptSucceeded: false }
                && client.Snapshot.HoldingRegistersReadState is { IsGood: true }
            : client.Snapshot.HoldingRegistersReadState is { Enabled: true, HasSuccessfulRead: false, LastAttemptSucceeded: false }
                && client.Snapshot.CoilsReadState is { IsGood: true }));

        var failed = failCoils ? client.Snapshot.CoilsReadState! : client.Snapshot.HoldingRegistersReadState!;
        Assert.Equal(0, failed.SuccessfulReadSequence);
        Assert.Equal(DateTimeOffset.MinValue, failed.LastSuccessfulReadAt);
        Assert.Equal(ModbusConnectionState.Running, client.State);
    }

    [Fact]
    public async Task RegisterBitWrite_RejectsOldShadowAfterFailedReadInSameSession()
    {
        await using var peer = new ControlledModbusPeer();
        var options = CreateFacadeOptions(peer.Port);
        options.WriteConfirmationTimeoutMs = 0;
        options.DataMap.RemoveAll(point => point.Name == "reg");
        options.DataMap.Add(new ModbusDataPointOptions
        {
            Name = "Bit0", Area = ModbusDataArea.HoldingRegister, Address = 0,
            Length = 1, BitIndex = 1, Type = ModbusValueType.Bool, Access = ModbusDataAccess.ReadWrite
        });
        await using var client = new ModbusClientService(NullLogger<ModbusClientService>.Instance);
        await using var server = new ModbusServerService(NullLogger<ModbusServerService>.Instance);
        var monitor = new TestOptionsMonitor(options);
        await using var runtime = new ModbusRuntimeService(client, server, monitor, NullLogger<ModbusRuntimeService>.Instance);
        await using var service = new ModbusTcpService(runtime, client, server, monitor,
            new ModbusDataMapValidator(), NullLogger<ModbusTcpService>.Instance);
        Assert.True((await service.StartClientAsync()).Succeeded);
        Assert.True(await WaitForAsync(() => service.CurrentSnapshot.HoldingRegistersReadState is { IsGood: true }));
        var session = service.CurrentSnapshot.SourceSessionId;
        peer.FailFunctionCode = 3;
        Assert.True(await WaitForAsync(() => service.CurrentSnapshot.HoldingRegistersReadState is
            { HasSuccessfulRead: true, LastAttemptSucceeded: false }));
        Assert.Equal(session, service.CurrentSnapshot.SourceSessionId);
        Assert.Equal(ModbusConnectionState.Running, service.State.ClientState);
        var result = await service.SetAsync("Bit0", true);
        Assert.False(result.Succeeded);
        Assert.Equal("ModbusRegisterShadowUnavailable", result.ErrorCode);
        Assert.Equal(0, peer.WriteRequests);
    }

    private static ModbusEndpointOptions Endpoint(int port, int coils, int registers) => new()
    {
        Enabled = true,
        Host = IPAddress.Loopback.ToString(),
        Port = port,
        UnitId = 1,
        PollIntervalMs = 100,
        CoilCount = coils,
        RegisterCount = registers
    };

    private static ModbusOptions CreateFacadeOptions(int clientPort) => new()
    {
        Client = Endpoint(clientPort, coils: 1, registers: 1),
        Server = Endpoint(GetAvailablePort(), coils: 1, registers: 1),
        DataMap =
        [
            new ModbusDataPointOptions
            {
                Name = "coil", Area = ModbusDataArea.Coil, Address = 0, Length = 1,
                Type = ModbusValueType.Bool, Access = ModbusDataAccess.Read
            },
            new ModbusDataPointOptions
            {
                Name = "reg", Area = ModbusDataArea.HoldingRegister, Address = 0, Length = 1,
                Type = ModbusValueType.UInt16, Access = ModbusDataAccess.Read
            }
        ]
    };

    private static async Task<bool> WaitForAsync(Func<bool> predicate)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < until)
        {
            if (predicate())
                return true;
            await Task.Delay(20);
        }
        return predicate();
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class ControlledModbusPeer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(20));
        private readonly Task _serveTask;
        private int _failFunctionCode;
        private int _failedCoilReads;
        private int _failedRegisterReads;
        private int _writeRequests;

        public ControlledModbusPeer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serveTask = ServeAsync();
        }

        public int Port { get; }
        public int FailFunctionCode
        {
            get => Volatile.Read(ref _failFunctionCode);
            set => Volatile.Write(ref _failFunctionCode, value);
        }
        public int FailedCoilReads => Volatile.Read(ref _failedCoilReads);
        public int FailedRegisterReads => Volatile.Read(ref _failedRegisterReads);
        public int WriteRequests => Volatile.Read(ref _writeRequests);

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            try { await _serveTask.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
            catch (EndOfStreamException) { }
            _stop.Dispose();
        }

        private async Task ServeAsync()
        {
            using var socket = await _listener.AcceptTcpClientAsync(_stop.Token);
            var stream = socket.GetStream();
            while (!_stop.IsCancellationRequested)
            {
                var header = new byte[7];
                await stream.ReadExactlyAsync(header, _stop.Token);
                var request = new byte[BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4)) - 1];
                await stream.ReadExactlyAsync(request, _stop.Token);

                if (request[0] is 5 or 6 or 15 or 16)
                    Interlocked.Increment(ref _writeRequests);
                byte[] response;
                if (request[0] == 1 && FailFunctionCode == 1)
                {
                    Interlocked.Increment(ref _failedCoilReads);
                    response = [0x81, 2];
                }
                else if (request[0] == 3 && FailFunctionCode == 3)
                {
                    Interlocked.Increment(ref _failedRegisterReads);
                    response = [0x83, 2];
                }
                else if (request[0] == 1)
                {
                    response = [1, 1, 1];
                }
                else if (request[0] == 3)
                {
                    response = [3, 2, 0, 5];
                }
                else
                {
                    response = [(byte)(request[0] | 0x80), 1];
                }

                BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), checked((ushort)(response.Length + 1)));
                await stream.WriteAsync(header, _stop.Token);
                await stream.WriteAsync(response, _stop.Token);
            }
        }
    }

    private sealed class TestOptionsMonitor(ModbusOptions currentValue) : IOptionsMonitor<ModbusOptions>
    {
        public ModbusOptions CurrentValue { get; } = currentValue;
        public ModbusOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<ModbusOptions, string?> listener) => null;
    }

    private sealed class RecordingSignalObserver : IObserver<IReadOnlyDictionary<string, SignalValue>>
    {
        public IReadOnlyDictionary<string, SignalValue> Latest { get; private set; }
            = new Dictionary<string, SignalValue>(StringComparer.OrdinalIgnoreCase);
        public void OnCompleted() { }
        public void OnError(Exception error) => throw error;
        public void OnNext(IReadOnlyDictionary<string, SignalValue> value) => Latest = value;
    }
}
