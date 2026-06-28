using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Infrastructure.Modbus.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed class ModbusBitWriterTests
{
    [Fact]
    public async Task PulseAsync_WritesCoilTrueThenFalseThroughClient()
    {
        var runtime = new FakeRuntime(ModbusConnectionState.Running, ModbusConnectionState.Stopped);
        var client = new RecordingClient();
        var writer = CreateWriter(runtime, client, new RecordingServer());

        var result = await writer.PulseAsync(new ModbusBitAddressOptions
        {
            Area = ModbusDataArea.Coil,
            Address = 2
        }, pulseDurationMs: 1);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal([(2, true), (2, false)], client.CoilWrites);
    }

    [Fact]
    public async Task PulseAsync_PreservesRegisterBitsFromSnapshot()
    {
        var runtime = new FakeRuntime(ModbusConnectionState.Running, ModbusConnectionState.Stopped)
        {
            ClientSnapshot = new ModbusSnapshot
            {
                Role = ModbusRuntimeRole.Client,
                HoldingRegisters = [0b_0010]
            }
        };
        var client = new RecordingClient();
        var writer = CreateWriter(runtime, client, new RecordingServer());

        var result = await writer.PulseAsync(new ModbusBitAddressOptions
        {
            Area = ModbusDataArea.HoldingRegister,
            Address = 0,
            BitIndex = 0
        }, pulseDurationMs: 1);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal((0, (ushort)0b_0011), client.RegisterWrites[0]);
        Assert.Equal((0, (ushort)0b_0010), client.RegisterWrites[1]);
    }

    [Fact]
    public async Task PulseAsync_WritesThroughServerWhenOnlyServerRuns()
    {
        var runtime = new FakeRuntime(ModbusConnectionState.Stopped, ModbusConnectionState.Running);
        var server = new RecordingServer();
        var writer = CreateWriter(runtime, new RecordingClient(), server);

        var result = await writer.PulseAsync(new ModbusBitAddressOptions
        {
            Area = ModbusDataArea.Coil,
            Address = 1
        }, pulseDurationMs: 1);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal([(1, true), (1, false)], server.CoilWrites);
    }

    private static ModbusBitWriter CreateWriter(
        IModbusRuntimeService runtime,
        IModbusClientService client,
        IModbusServerService server)
        => new(runtime, client, server, NullLogger<ModbusBitWriter>.Instance);

    private sealed class FakeRuntime(
        ModbusConnectionState clientState,
        ModbusConnectionState serverState) : IModbusRuntimeService
    {
        public ModbusStatus Status { get; } = new()
        {
            ClientState = clientState,
            ServerState = serverState
        };
        public ModbusSnapshot ClientSnapshot { get; init; } = ModbusSnapshot.Empty;
        public ModbusSnapshot ServerSnapshot { get; init; } = ModbusSnapshot.Empty;
        public ModbusOptions CurrentOptions { get; } = new();
        public event EventHandler<ModbusStatus>? StatusChanged { add { } remove { } }
        public event EventHandler<ModbusSnapshot>? SnapshotChanged { add { } remove { } }
        public Task StartAsync(ModbusRunMode mode = ModbusRunMode.Both, ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestartAsync(ModbusRunMode mode = ModbusRunMode.Both, ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopClientAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopServerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingClient : IModbusClientService
    {
        public ModbusConnectionState State => ModbusConnectionState.Running;
        public ModbusSnapshot Snapshot => ModbusSnapshot.Empty;
        public List<(int Address, bool Value)> CoilWrites { get; } = [];
        public List<(int Address, ushort Value)> RegisterWrites { get; } = [];
        public event EventHandler<ModbusStatus>? StatusChanged { add { } remove { } }
        public event EventHandler<ModbusSnapshot>? SnapshotChanged { add { } remove { } }
        public Task StartAsync(ModbusEndpointOptions options, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteCoilAsync(int address, bool value, CancellationToken cancellationToken = default)
        {
            CoilWrites.Add((address, value));
            return Task.CompletedTask;
        }

        public Task WriteRegisterAsync(int address, ushort value, CancellationToken cancellationToken = default)
        {
            RegisterWrites.Add((address, value));
            return Task.CompletedTask;
        }

        public Task WriteRegistersAsync(int startAddress, ushort[] values, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingServer : IModbusServerService
    {
        public ModbusConnectionState State => ModbusConnectionState.Running;
        public ModbusSnapshot Snapshot => ModbusSnapshot.Empty;
        public List<(int Address, bool Value)> CoilWrites { get; } = [];
        public List<(int Address, ushort Value)> RegisterWrites { get; } = [];
        public event EventHandler<ModbusStatus>? StatusChanged { add { } remove { } }
        public event EventHandler<ModbusSnapshot>? SnapshotChanged { add { } remove { } }
        public Task StartAsync(ModbusEndpointOptions options, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetCoilAsync(int address, bool value, CancellationToken cancellationToken = default)
        {
            CoilWrites.Add((address, value));
            return Task.CompletedTask;
        }

        public Task SetRegisterAsync(int address, ushort value, CancellationToken cancellationToken = default)
        {
            RegisterWrites.Add((address, value));
            return Task.CompletedTask;
        }

        public Task SetRegistersAsync(int startAddress, ushort[] values, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
