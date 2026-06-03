using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Infrastructure.Modbus.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed class ServerServiceTests
{
    [Fact]
    public async Task Server_StartsWithConfiguredSizesAndDoesNotMutateValues()
    {
        var port = GetAvailablePort();
        await using var server = new ModbusServerService(
            NullLogger<ModbusServerService>.Instance);

        await server.StartAsync(new ModbusEndpointOptions
        {
            Enabled = true,
            Port = port,
            UnitId = 1,
            PollIntervalMs = 100,
            CoilCount = 7,
            RegisterCount = 11
        });

        await Task.Delay(250);

        var first = server.Snapshot;
        Assert.Equal(7, first.Coils.Count);
        Assert.Equal(11, first.HoldingRegisters.Count);
        Assert.All(first.Coils, Assert.False);
        Assert.All(first.HoldingRegisters, value => Assert.Equal((ushort)0, value));

        await Task.Delay(250);
        var second = server.Snapshot;
        Assert.Equal(first.Coils, second.Coils);
        Assert.Equal(first.HoldingRegisters, second.HoldingRegisters);
    }

    [Fact]
    public async Task Server_SetMethodsUpdateLocalSnapshot()
    {
        var port = GetAvailablePort();
        await using var server = new ModbusServerService(
            NullLogger<ModbusServerService>.Instance);

        await server.StartAsync(new ModbusEndpointOptions
        {
            Enabled = true,
            Port = port,
            UnitId = 1,
            PollIntervalMs = 100,
            CoilCount = 6,
            RegisterCount = 8
        });

        await server.SetCoilAsync(2, true);
        await server.SetRegisterAsync(3, 1234);
        await server.SetRegistersAsync(4, new ushort[] { 55, 66 });

        Assert.True(server.Snapshot.Coils[2]);
        Assert.Equal((ushort)1234, server.Snapshot.HoldingRegisters[3]);
        Assert.Equal((ushort)55, server.Snapshot.HoldingRegisters[4]);
        Assert.Equal((ushort)66, server.Snapshot.HoldingRegisters[5]);
    }

    [Fact]
    public async Task Server_StopAsyncIsIdempotent()
    {
        var port = GetAvailablePort();
        await using var server = new ModbusServerService(
            NullLogger<ModbusServerService>.Instance);

        await server.StartAsync(new ModbusEndpointOptions
        {
            Enabled = true,
            Port = port,
            UnitId = 1,
            PollIntervalMs = 100,
            CoilCount = 6,
            RegisterCount = 8
        });

        await server.StopAsync();
        await server.StopAsync();

        Assert.Equal(ModbusConnectionState.Stopped, server.State);
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
