using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Dialogs.EquipmentCardParametersDialog;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Configurator.Desktop.Workspace.RouteMap.Settings;
using Configurator.Infrastructure.Modbus.Client;
using Configurator.Infrastructure.Modbus.RouteMap;
using Configurator.Infrastructure.Modbus.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed class StabilityAcceptanceIntegrationTests
{
    private const string Setpoint = "equip.bucket.parameter";

    [Fact]
    public async Task Mock_pending_setpoint_uses_first_Modbus_snapshot_and_partial_area_quality_recovers_without_extra_write()
    {
        await using var peer = new LoopbackPeer();
        var options = CreateOptions(peer.Port);
        var monitor = new MutableOptionsMonitor(options);
        await using var client = new ModbusClientService(NullLogger<ModbusClientService>.Instance);
        await using var server = new ModbusServerService(NullLogger<ModbusServerService>.Instance);
        await using var protocolRuntime = new ModbusRuntimeService(client, server, monitor,
            NullLogger<ModbusRuntimeService>.Instance);
        await using var facade = new ModbusTcpService(protocolRuntime, client, server, monitor,
            new ModbusDataMapValidator(), NullLogger<ModbusTcpService>.Instance);
        using var provider = new ModbusTcpSignalValueProvider(facade, facade, monitor,
            Options.Create(new RouteMapRuntimeOptions { StaleAfterMs = 60000 }),
            NullLogger<ModbusTcpSignalValueProvider>.Instance);
        var observed = new RecordingObserver();
        using var observation = provider.Observe().Subscribe(observed);
        using var mock = new ManualProvider();
        var modbusDispatcher = new ModbusTcpCommandDispatcher(facade, monitor);
        using var routeRuntime = new RouteMapSignalRuntime(mock, new RecordingDispatcher(), provider,
            modbusDispatcher, RouteMapSignalSource.Mock);
        var store = new PendingStore();
        var auto = new EquipmentParameterAutoDispatcher(store, routeRuntime,
            NoopValidator.Instance, routeRuntime);

        await store.SaveAsync("bucket", [new(Setpoint, "5")], pendingAutoDispatch: true);
        await auto.DispatchPendingAsync();
        Assert.Empty(peer.Writes);

        Assert.True((await facade.StartClientAsync(options)).Succeeded);
        Assert.True(await WaitForAsync(() => facade.CurrentSnapshot.CoilsReadState is { IsGood: true }
            && facade.CurrentSnapshot.HoldingRegistersReadState is { IsGood: true }
            && observed.Latest.GetValueOrDefault(RouteMapSystemSignalIds.ConnectionConnected)?.Value is true));
        routeRuntime.SwitchSource(RouteMapSignalSource.Modbus);
        await auto.DispatchPendingAsync();

        Assert.Equal((ushort)5, Assert.Single(peer.Writes));
        Assert.False(Assert.Single(store.GetPendingOrAll()).PendingAutoDispatch);

        var initialCoilRead = peer.CoilReads;
        peer.FailCoils = true;
        Assert.True(await WaitForAsync(() => peer.CoilReads > initialCoilRead
            && observed.Latest.GetValueOrDefault("enabled") is { IsQualityGood: false, IsStale: true }
            && observed.Latest.GetValueOrDefault("holding") is { IsQualityGood: true, IsStale: false }));
        Assert.Single(peer.Writes);
        Assert.True(observed.Latest[RouteMapSystemSignalIds.ConnectionConnected].Value is true);

        var failedCoilSequence = facade.CurrentSnapshot.CoilsReadState!.SuccessfulReadSequence;
        peer.FailCoils = false;
        Assert.True(await WaitForAsync(() => facade.CurrentSnapshot.CoilsReadState is { IsGood: true }
            && facade.CurrentSnapshot.CoilsReadState.SuccessfulReadSequence > failedCoilSequence
            && observed.Latest.GetValueOrDefault("enabled") is { IsQualityGood: true, IsStale: false }));
        Assert.Single(peer.Writes);

        Assert.True((await facade.StopAsync()).Succeeded);
        routeRuntime.SwitchSource(RouteMapSignalSource.Mock);
        await store.SaveAsync("bucket", [new(Setpoint, "6")], pendingAutoDispatch: true);
        Assert.Single(peer.Writes);
        routeRuntime.SwitchSource(RouteMapSignalSource.Modbus);
        await auto.DispatchPendingAsync();
        Assert.Single(peer.Writes);
        Assert.True((await facade.StartClientAsync(options)).Succeeded);
        Assert.True(await WaitForAsync(() => routeRuntime.TryGetAvailableModbusGeneration(out _)));
        await auto.DispatchPendingAsync();
        await auto.DispatchPendingAsync();
        Assert.Equal(new ushort[] { 5, 6 }, peer.Writes);
    }

    private static ModbusOptions CreateOptions(int port) => new()
    {
        Client = new ModbusEndpointOptions
        {
            Enabled = true, Host = IPAddress.Loopback.ToString(), Port = port, UnitId = 1,
            PollIntervalMs = 100, CoilCount = 1, RegisterCount = 2
        },
        Server = new ModbusEndpointOptions { Enabled = false },
        DataMap =
        [
            new ModbusDataPointOptions
            {
                Name = "enabled", Area = ModbusDataArea.Coil, Address = 0, Length = 1,
                Type = ModbusValueType.Bool, Access = ModbusDataAccess.Read
            },
            new ModbusDataPointOptions
            {
                Name = "holding", Area = ModbusDataArea.HoldingRegister, Address = 0, Length = 1,
                Type = ModbusValueType.UInt16, Access = ModbusDataAccess.Read
            },
            new ModbusDataPointOptions
            {
                Name = Setpoint, Area = ModbusDataArea.HoldingRegister, Address = 1, Length = 1,
                Type = ModbusValueType.UInt16, Access = ModbusDataAccess.ReadWrite
            }
        ]
    };

    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        var timeout = DateTimeOffset.UtcNow.AddSeconds(6);
        while (DateTimeOffset.UtcNow < timeout)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    private sealed class MutableOptionsMonitor(ModbusOptions currentValue) : IOptionsMonitor<ModbusOptions>
    {
        public ModbusOptions CurrentValue { get; } = currentValue;
        public ModbusOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<ModbusOptions, string?> listener) => null;
    }

    private sealed class ManualProvider : ISignalValueProvider, IDisposable
    {
        private readonly Subject<IReadOnlyDictionary<string, SignalValue>> _values = new();
        public IObservable<IReadOnlyDictionary<string, SignalValue>> Observe() => _values;
        public void Dispose() => _values.Dispose();
    }

    private sealed class RecordingObserver : IObserver<IReadOnlyDictionary<string, SignalValue>>
    {
        public IReadOnlyDictionary<string, SignalValue> Latest { get; private set; }
            = new Dictionary<string, SignalValue>(StringComparer.OrdinalIgnoreCase);
        public void OnCompleted() { }
        public void OnError(Exception error) => throw error;
        public void OnNext(IReadOnlyDictionary<string, SignalValue> value) => Latest = value;
    }

    private sealed class RecordingDispatcher : IEquipmentCommandDispatcher
    {
        public Task DispatchAsync(SignalWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopValidator : IEquipmentParameterWriteValidator
    {
        public static NoopValidator Instance { get; } = new();
        public string? Validate(SignalWriteRequest request) => null;
    }

    private sealed class PendingStore : IEquipmentParameterValueStore
    {
        private readonly List<EquipmentCardParameter> _parameters = [];
        public Task SaveAsync(string cardId, IReadOnlyCollection<EquipmentParameterSetpoint> values,
            bool pendingAutoDispatch, CancellationToken cancellationToken = default)
        {
            foreach (var value in values)
            {
                var old = _parameters.FindIndex(p => p.Binding.SignalId == value.SignalId);
                var item = new EquipmentCardParameter(value.SignalId,
                    new SignalBinding(SignalBindingRole.EquipmentParameter, value.SignalId,
                        SignalBindingDirection.ReadWrite, SignalValueType.UInt16),
                    SavedValue: value.Value, PendingAutoDispatch: pendingAutoDispatch);
                if (old >= 0) _parameters[old] = item; else _parameters.Add(item);
            }
            return Task.CompletedTask;
        }
        public IReadOnlyList<PendingEquipmentParameter> GetPending() => _parameters
            .Where(p => p.PendingAutoDispatch).Select(p => new PendingEquipmentParameter("bucket", p)).ToArray();
        public EquipmentCardParameter[] GetPendingOrAll() => _parameters.ToArray();
        public Task<bool> TryBeginDispatchAsync(string cardId, string signalId, string expectedSavedValue,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = _parameters.FindIndex(p => p.Binding.SignalId == signalId && p.SavedValue == expectedSavedValue
                && p.PendingAutoDispatch);
            if (index < 0) return Task.FromResult(false);
            _parameters[index] = _parameters[index] with
            {
                PendingAutoDispatch = false,
                LastDispatchError = "Исход автоматической отправки неизвестен."
            };
            return Task.FromResult(true);
        }
        public Task CompleteDispatchAsync(string cardId, string signalId, string expectedSavedValue,
            string? errorMessage, CancellationToken cancellationToken = default)
        {
            var index = _parameters.FindIndex(p => p.Binding.SignalId == signalId);
            if (index >= 0 && _parameters[index].SavedValue == expectedSavedValue)
                _parameters[index] = _parameters[index] with { PendingAutoDispatch = false, LastDispatchError = errorMessage };
            return Task.CompletedTask;
        }
    }

    private sealed class LoopbackPeer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(25));
        private readonly Task _serve;
        private int _failCoils;
        private int _coilReads;
        private ushort _setpoint;
        private readonly List<ushort> _writes = [];
        private readonly object _sync = new();

        public LoopbackPeer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serve = ServeAsync();
        }
        public int Port { get; }
        public bool FailCoils { get => Volatile.Read(ref _failCoils) != 0; set => Volatile.Write(ref _failCoils, value ? 1 : 0); }
        public int CoilReads => Volatile.Read(ref _coilReads);
        public ushort[] Writes { get { lock (_sync) return _writes.ToArray(); } }
        public async ValueTask DisposeAsync()
        {
            _stop.Cancel(); _listener.Stop();
            try { await _serve.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException or EndOfStreamException) { }
            _stop.Dispose();
        }
        private async Task ServeAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                using var socket = await _listener.AcceptTcpClientAsync(_stop.Token);
                var stream = socket.GetStream();
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        var header = new byte[7];
                        await stream.ReadExactlyAsync(header, _stop.Token);
                        var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4)) - 1;
                        var request = new byte[length];
                        await stream.ReadExactlyAsync(request, _stop.Token);
                        byte[] response;
                        if (request[0] == 1)
                        {
                            Interlocked.Increment(ref _coilReads);
                            response = FailCoils ? [0x81, 2] : [1, 1, 1];
                        }
                        else if (request[0] == 3)
                        {
                            ushort setpoint;
                            lock (_sync) setpoint = _setpoint;
                            response = [3, 4, 0, 5, (byte)(setpoint >> 8), (byte)setpoint];
                        }
                        else if (request[0] == 6)
                        {
                            var address = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(1));
                            var value = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(3));
                            lock (_sync) { _writes.Add(value); _setpoint = value; }
                            response = [6, (byte)(address >> 8), (byte)address, (byte)(value >> 8), (byte)value];
                        }
                        else response = [(byte)(request[0] | 0x80), 1];
                        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), checked((ushort)(response.Length + 1)));
                        await stream.WriteAsync(header, _stop.Token);
                        await stream.WriteAsync(response, _stop.Token);
                    }
                }
                catch (Exception ex) when (ex is IOException or SocketException)
                {
                }
            }
        }
    }
}
