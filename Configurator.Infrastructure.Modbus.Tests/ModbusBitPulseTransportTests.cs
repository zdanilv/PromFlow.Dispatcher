using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Infrastructure.Modbus.Client;
using Configurator.Infrastructure.Modbus.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed class ModbusBitPulseTransportTests
{
    private const int UnitId = 17;
    private const int AddressBase = 20;
    private const int BitOffset = 2;
    private const int PhysicalAddress = AddressBase + BitOffset;
    private const int BitIndex = 3;

    [Fact]
    public async Task Client_RegisterPulseReadsBothEdgesAtThePhysicalAddressAndKeepsNeighborChanges()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        using var listener = Listen();
        await using var client = CreateClient();
        await using var server = CreateServer();
        using var peer = await ConnectClientAsync(client, listener, ModbusDataArea.HoldingRegister, ct);
        using var stream = peer.GetStream();
        var writer = CreateWriter(client, server);

        var pulse = writer.PulseAsync(Address(ModbusDataArea.HoldingRegister), 1, ct);
        var firstRead = await ReadRequestAsync(stream, 3, PhysicalAddress, 1, ct);
        await RespondRegisterAsync(stream, firstRead, 0x0002, ct);
        var rise = await ReadRequestAsync(stream, 6, PhysicalAddress, 0x000A, ct);
        await stream.WriteAsync(rise, ct);

        var releaseRead = await ReadRequestAsync(stream, 3, PhysicalAddress, 1, ct);
        // The device changed neighboring bits after applying true.
        await RespondRegisterAsync(stream, releaseRead, 0x0048, ct);
        var release = await ReadRequestAsync(stream, 6, PhysicalAddress, 0x0040, ct);
        await stream.WriteAsync(release, ct);

        var result = await pulse.WaitAsync(ct);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.False(stream.DataAvailable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Client_FailedFreshRegisterReadDoesNotWriteUsingStaleSnapshot(bool failOnRise)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        using var listener = Listen();
        await using var client = CreateClient();
        await using var server = CreateServer();
        using var peer = await ConnectClientAsync(client, listener, ModbusDataArea.HoldingRegister, ct);
        using var stream = peer.GetStream();
        var runtime = new ModbusBitWriterTests.FakeRuntime(true)
        {
            ClientSnapshot = new ModbusSnapshot { Role = ModbusRuntimeRole.Client, HoldingRegisters = [0, 0, 0x20] },
            ServerSnapshot = new ModbusSnapshot { Role = ModbusRuntimeRole.Server, HoldingRegisters = [0, 0, 0x40] }
        };
        var writer = new ModbusBitWriter(runtime, client, server, NullLogger<ModbusBitWriter>.Instance);

        var pulse = writer.PulseAsync(Address(ModbusDataArea.HoldingRegister), 1, ct);
        var read = await ReadRequestAsync(stream, 3, PhysicalAddress, 1, ct);
        if (!failOnRise)
        {
            await RespondRegisterAsync(stream, read, 0x0002, ct);
            var rise = await ReadRequestAsync(stream, 6, PhysicalAddress, 0x000A, ct);
            await stream.WriteAsync(rise, ct);
            read = await ReadRequestAsync(stream, 3, PhysicalAddress, 1, ct);
        }
        await RespondExceptionAsync(stream, read, 2, ct);

        var result = await pulse.WaitAsync(ct);
        Assert.False(result.Succeeded);
        Assert.Equal(failOnRise ? "ModbusBitPulseRisingEdgeFailed" : "ModbusBitPulseReleaseFailed", result.ErrorCode);
        Assert.False(stream.DataAvailable);
    }

    [Theory]
    [InlineData(ModbusDataArea.Coil)]
    [InlineData(ModbusDataArea.HoldingRegister)]
    public async Task Client_AppliedActivationWithMissingReplyIsNotResent(ModbusDataArea area)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        using var listener = Listen();
        await using var client = CreateClient();
        await using var server = CreateServer();
        using var peer = await ConnectClientAsync(client, listener, area, ct);
        using var stream = peer.GetStream();
        var writer = CreateWriter(client, server);
        var isRegister = area == ModbusDataArea.HoldingRegister;

        var pulse = writer.PulseAsync(Address(area), 1, ct);
        if (isRegister)
        {
            var read = await ReadRequestAsync(stream, 3, PhysicalAddress, 1, ct);
            await RespondRegisterAsync(stream, read, 0x0002, ct);
        }
        // The fixture accepts and applies the one activation request but never acknowledges it.
        var function = isRegister ? 6 : 5;
        var applied = isRegister ? 0x000A : 0xFF00;
        await ReadRequestAsync(stream, function, PhysicalAddress, applied, ct);

        var result = await pulse.WaitAsync(ct);
        Assert.False(result.Succeeded);
        Assert.Equal("ModbusBitPulseReleaseFailed", result.ErrorCode);
        Assert.Contains("unknown", result.ErrorMessage);
        Assert.Equal(0, await stream.ReadAsync(new byte[12], ct));
    }

    [Theory]
    [InlineData(ModbusDataArea.Coil)]
    [InlineData(ModbusDataArea.HoldingRegister)]
    public async Task Client_FailedReleaseReplyDoesNotReportSuccess(ModbusDataArea area)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        using var listener = Listen();
        await using var client = CreateClient();
        await using var server = CreateServer();
        using var peer = await ConnectClientAsync(client, listener, area, ct);
        using var stream = peer.GetStream();
        var writer = CreateWriter(client, server);
        var isRegister = area == ModbusDataArea.HoldingRegister;

        var pulse = writer.PulseAsync(Address(area), 1, ct);
        if (isRegister)
        {
            var read = await ReadRequestAsync(stream, 3, PhysicalAddress, 1, ct);
            await RespondRegisterAsync(stream, read, 0x0002, ct);
        }
        var function = isRegister ? 6 : 5;
        var rise = await ReadRequestAsync(stream, function, PhysicalAddress, isRegister ? 0x000A : 0xFF00, ct);
        await stream.WriteAsync(rise, ct);
        if (isRegister)
        {
            var read = await ReadRequestAsync(stream, 3, PhysicalAddress, 1, ct);
            await RespondRegisterAsync(stream, read, 0x0048, ct);
        }
        var release = await ReadRequestAsync(stream, function, PhysicalAddress, isRegister ? 0x40 : 0, ct);
        await RespondExceptionAsync(stream, release, 4, ct);

        var result = await pulse.WaitAsync(ct);
        Assert.False(result.Succeeded);
        Assert.Equal("ModbusBitPulseReleaseFailed", result.ErrorCode);
        Assert.False(stream.DataAvailable);
    }

    [Fact]
    public async Task Client_CapturedTargetRejectsReleaseAfterEndpointReplacement()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        using var originalListener = Listen();
        using var replacementListener = Listen();
        await using var client = CreateClient();
        using var peer = await ConnectClientAsync(client, originalListener, ModbusDataArea.Coil, ct);
        using var stream = peer.GetStream();
        var address = Address(ModbusDataArea.Coil);
        var provider = (IModbusBitPulseProvider)client;
        var target = provider.CaptureBitPulseTarget(address);

        var activation = target.WriteAsync(true, ct);
        var rise = await ReadRequestAsync(stream, 5, PhysicalAddress, 0xFF00, ct);
        await stream.WriteAsync(rise, ct);
        await activation.WaitAsync(ct);
        await client.StopAsync(ct);
        using var replacementPeer = await ConnectClientAsync(client, replacementListener, ModbusDataArea.Coil, ct);
        using var replacementStream = replacementPeer.GetStream();

        await Assert.ThrowsAsync<ModbusBitPulseNotSentException>(() => target.WriteAsync(false, ct));

        var newActivation = provider.CaptureBitPulseTarget(address).WriteAsync(true, ct);
        var replacementRise = await ReadRequestAsync(replacementStream, 5, PhysicalAddress, 0xFF00, ct);
        await replacementStream.WriteAsync(replacementRise, ct);
        await newActivation.WaitAsync(ct);
    }

    [Fact]
    public async Task Server_CapturedTargetUpdatesLiveRegisterWordAndRejectsReplacementDataStore()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        await using var server = CreateServer();
        await using var client = CreateClient();
        var options = new ModbusEndpointOptions
        {
            Enabled = true,
            BindAddress = IPAddress.Loopback.ToString(),
            Port = GetFreePort(),
            UnitId = UnitId,
            PollIntervalMs = 100,
            RegisterCount = 4
        };
        await server.StartAsync(options, ct);
        Assert.Equal(ModbusConnectionState.Running, server.State);
        await server.SetRegisterAsync(BitOffset, 0x0002, ct);
        var provider = (IModbusBitPulseProvider)server;
        var target = provider.CaptureBitPulseTarget(Address(ModbusDataArea.HoldingRegister));

        await target.WriteAsync(true, ct);
        var readOptions = options.Clone();
        readOptions.Host = IPAddress.Loopback.ToString();
        readOptions.CoilsEnabled = false;
        await client.StartAsync(readOptions, ct);
        await WaitForRegisterAsync(client, 0x000A, ct);
        await client.WriteRegisterAsync(BitOffset, 0x0048, ct);
        await target.WriteAsync(false, ct);
        await WaitForRegisterAsync(client, 0x0040, ct);

        await client.StopAsync(ct);
        await server.StopAsync(ct);
        options.Port = GetFreePort();
        await server.StartAsync(options, ct);
        await server.SetRegisterAsync(BitOffset, 0x0108, ct);

        await Assert.ThrowsAsync<ModbusBitPulseNotSentException>(() => target.WriteAsync(false, ct));

        readOptions.Port = options.Port;
        await client.StartAsync(readOptions, ct);
        await WaitForRegisterAsync(client, 0x0108, ct);
    }

    private static ModbusBitAddressOptions Address(ModbusDataArea area) => new()
    {
        Area = area,
        Address = BitOffset,
        BitIndex = area == ModbusDataArea.HoldingRegister ? BitIndex : null
    };

    private static ModbusBitWriter CreateWriter(ModbusClientService client, ModbusServerService server)
        => new(new ModbusBitWriterTests.FakeRuntime(true), client, server, NullLogger<ModbusBitWriter>.Instance);

    private static ModbusClientService CreateClient() => new(NullLogger<ModbusClientService>.Instance);
    private static ModbusServerService CreateServer() => new(NullLogger<ModbusServerService>.Instance);

    private static async Task<TcpClient> ConnectClientAsync(
        ModbusClientService client, TcpListener listener, ModbusDataArea area, CancellationToken ct)
    {
        var isRegister = area == ModbusDataArea.HoldingRegister;
        await client.StartAsync(new ModbusEndpointOptions
        {
            Host = IPAddress.Loopback.ToString(),
            Port = ((IPEndPoint)listener.LocalEndpoint).Port,
            UnitId = UnitId,
            PollIntervalMs = 60_000,
            CoilsEnabled = !isRegister,
            CoilStartAddress = AddressBase,
            CoilCount = 4,
            HoldingRegistersEnabled = isRegister,
            HoldingRegisterStartAddress = AddressBase,
            RegisterCount = 4
        }, ct);
        var peer = await listener.AcceptTcpClientAsync(ct);
        try
        {
            var stream = peer.GetStream();
            var poll = await ReadRequestAsync(stream, isRegister ? 3 : 1, AddressBase, 4, ct);
            byte[] payload = isRegister ? [3, 8, 0, 0, 0, 0, 0, 0x20, 0, 0] : [1, 1, 0];
            await RespondAsync(stream, poll, payload, ct);
            while (client.State != ModbusConnectionState.Running)
                await Task.Delay(10, ct);
            return peer;
        }
        catch
        {
            peer.Dispose();
            throw;
        }
    }

    private static TcpListener Listen()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return listener;
    }

    private static int GetFreePort()
    {
        using var listener = Listen();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task WaitForRegisterAsync(ModbusClientService client, ushort expected, CancellationToken ct)
    {
        while (client.Snapshot.HoldingRegisters.Count <= BitOffset || client.Snapshot.HoldingRegisters[BitOffset] != expected)
            await Task.Delay(10, ct);
    }

    private static async Task<byte[]> ReadRequestAsync(
        NetworkStream stream, int function, int address, int data, CancellationToken ct)
    {
        var request = new byte[12];
        await stream.ReadExactlyAsync(request, ct);
        Assert.Equal(6, BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(4, 2)));
        Assert.Equal(UnitId, request[6]);
        Assert.Equal(function, request[7]);
        Assert.Equal(address, BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(8, 2)));
        Assert.Equal(data, BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(10, 2)));
        return request;
    }

    private static ValueTask RespondRegisterAsync(NetworkStream stream, byte[] request, ushort word, CancellationToken ct)
        => RespondAsync(stream, request, [3, 2, (byte)(word >> 8), (byte)(word & 0xFF)], ct);

    private static ValueTask RespondExceptionAsync(NetworkStream stream, byte[] request, byte code, CancellationToken ct)
        => RespondAsync(stream, request, [(byte)(request[7] | 0x80), code], ct);

    private static ValueTask RespondAsync(NetworkStream stream, byte[] request, byte[] payload, CancellationToken ct)
    {
        var response = new byte[7 + payload.Length];
        request.AsSpan(0, 7).CopyTo(response);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4, 2), checked((ushort)(1 + payload.Length)));
        payload.CopyTo(response, 7);
        return stream.WriteAsync(response, ct);
    }
}
