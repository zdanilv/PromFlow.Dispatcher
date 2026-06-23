using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Application.Services.Signals;
using Configurator.Infrastructure.Modbus.Archiving;
using Configurator.Infrastructure.Modbus.Client;
using Configurator.Infrastructure.Modbus.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed class ModbusTcpServiceTests
{
    [Fact]
    public async Task StartClientAsync_StopsServerBeforeStartingClient()
    {
        var runtime = new FakeRuntimeService();
        await using var service = CreateService(runtime, new NoopClientService(), new NoopServerService(), CreateOptions());

        var result = await service.StartClientAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(["StopServer", "StartClient"], runtime.Calls);
    }

    [Fact]
    public async Task StartServerAsync_StopsClientBeforeStartingServer()
    {
        var runtime = new FakeRuntimeService();
        await using var service = CreateService(runtime, new NoopClientService(), new NoopServerService(), CreateOptions());

        var result = await service.StartServerAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(["StopClient", "StartServer"], runtime.Calls);
    }

    [Fact]
    public void PublicFacade_DoesNotExposeBothMode()
    {
        var methods = typeof(IModbusTcpService).GetMethods();

        Assert.DoesNotContain(methods, method => method.Name.Contains("Both", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(methods, method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(ModbusRunMode)));
    }

    [Fact]
    public async Task SetAsync_ReturnsTimeoutWhenReadableWriteIsNotConfirmed()
    {
        var options = CreateOptions();
        options.WriteConfirmationTimeoutMs = 50;
        var runtime = new FakeRuntimeService();
        await using var service = CreateService(runtime, new NoopClientService(), new NoopServerService(), options);
        await service.StartClientAsync();

        var result = await service.SetAsync("DemoInput", (ushort)5);

        Assert.False(result.Succeeded);
        Assert.Equal("ModbusWriteConfirmationTimeout", result.ErrorCode);
    }

    [Fact]
    public async Task SetAsync_WhenClientAndServerAreRunning_WritesThroughClient()
    {
        var options = CreateOptions();
        options.WriteConfirmationTimeoutMs = 0;
        var runtime = new FakeRuntimeService();
        var client = new NoopClientService();
        var server = new NoopServerService();
        await using var service = CreateService(runtime, client, server, options);
        runtime.PublishRunningRoles();

        var coilResult = await service.SetAsync("DemoButton", true);
        var registerResult = await service.SetAsync("DemoInput", (ushort)11);

        Assert.True(coilResult.Succeeded, coilResult.ErrorMessage);
        Assert.True(registerResult.Succeeded, registerResult.ErrorMessage);
        Assert.Single(client.CoilWrites);
        Assert.Equal((0, true), client.CoilWrites[0]);
        Assert.Single(client.RegisterWrites);
        Assert.Equal((1, (ushort)11), client.RegisterWrites[0]);
        Assert.Empty(server.CoilWrites);
        Assert.Empty(server.RegisterWrites);
    }

    [Fact]
    public async Task SetAsync_WithCommandContext_AuditsPhysicalClientPayloads()
    {
        var options = CreateOptions();
        options.WriteConfirmationTimeoutMs = 0;
        options.Client.CoilStartAddress = 100;
        options.Client.HoldingRegisterStartAddress = 400;
        var runtime = new FakeRuntimeService();
        var client = new NoopClientService();
        var audit = new RecordingCommandAuditService();
        await using var service = CreateService(runtime, client, new NoopServerService(), options, audit);
        var context = CreateCommandContext("DemoButton", SignalValueType.Bool, "true", ModbusWriteMode.Latched);
        runtime.PublishRunningRoles();

        var coilResult = await service.SetAsync("DemoButton", true, context);
        var registerResult = await service.SetAsync("DemoInput", (ushort)0x1234, context);

        Assert.True(coilResult.Succeeded, coilResult.ErrorMessage);
        Assert.True(registerResult.Succeeded, registerResult.ErrorMessage);
        Assert.Equal(2, audit.PhysicalWrites.Count);
        Assert.Equal((0, true), Assert.Single(client.CoilWrites));
        Assert.Equal(context.CommandId, audit.PhysicalWrites[0].CommandId);
        Assert.Equal(ModbusRuntimeRole.Client, audit.PhysicalWrites[0].Role);
        Assert.Equal(ModbusDataArea.Coil, audit.PhysicalWrites[0].Area);
        Assert.Equal(100, audit.PhysicalWrites[0].Address);
        Assert.Equal(1, audit.PhysicalWrites[0].Quantity);
        Assert.Equal([0x01], audit.PhysicalWrites[0].PayloadBlob);
        Assert.True(audit.PhysicalWrites[0].Succeeded);
        Assert.Equal(ModbusRuntimeRole.Client, audit.PhysicalWrites[1].Role);
        Assert.Equal(ModbusDataArea.HoldingRegister, audit.PhysicalWrites[1].Area);
        Assert.Equal(401, audit.PhysicalWrites[1].Address);
        Assert.Equal(1, audit.PhysicalWrites[1].Quantity);
        Assert.Equal([0x12, 0x34], audit.PhysicalWrites[1].PayloadBlob);
        Assert.True(audit.PhysicalWrites[1].Succeeded);
    }

    [Fact]
    public async Task SetAsync_RejectsReadOnlyDataPoint()
    {
        var options = CreateOptions();
        options.DataMap.Add(new ModbusDataPointOptions
        {
            Name = "ReadOnlyRegister",
            Area = ModbusDataArea.HoldingRegister,
            Address = 2,
            Type = ModbusValueType.UInt16,
            Access = ModbusDataAccess.Read
        });
        var runtime = new FakeRuntimeService();
        await using var service = CreateService(runtime, new NoopClientService(), new NoopServerService(), options);
        await service.StartServerAsync();

        var result = await service.SetAsync("ReadOnlyRegister", (ushort)5);

        Assert.False(result.Succeeded);
        Assert.Equal("ModbusDataPointNotWritable", result.ErrorCode);
    }

    [Fact]
    public async Task ServerMode_SetGetAndSubscribeUseConfiguredMap()
    {
        var port = GetAvailablePort();
        var options = CreateOptions(port);
        await using var client = new ModbusClientService(NullLogger<ModbusClientService>.Instance);
        await using var server = new ModbusServerService(NullLogger<ModbusServerService>.Instance);
        await using var runtime = new ModbusRuntimeService(
            client,
            server,
            new TestOptionsMonitor(options),
            NullLogger<ModbusRuntimeService>.Instance);
        await using var service = CreateService(runtime, client, server, options);

        var start = await service.StartServerAsync();
        Assert.True(start.Succeeded, start.ErrorMessage);

        ushort? subscribedValue = null;
        using var subscription = service.Subscribe("DemoInput", value => subscribedValue = Convert.ToUInt16(value.Value));

        var set = await service.SetAsync("DemoInput", (ushort)1);
        var get = await service.GetAsync<ushort>("DemoInput");

        Assert.True(set.Succeeded, set.ErrorMessage);
        Assert.True(get.Succeeded, get.ErrorMessage);
        Assert.Equal((ushort)1, get.Value);
        Assert.True(await WaitForAsync(() => subscribedValue == 1));
    }

    [Fact]
    public async Task ClientMode_WriteIsVisibleInExternalServer()
    {
        var port = GetAvailablePort();
        var options = CreateOptions(port);
        await using var externalServer = new ModbusServerService(NullLogger<ModbusServerService>.Instance);
        await externalServer.StartAsync(options.Server);

        await using var client = new ModbusClientService(NullLogger<ModbusClientService>.Instance);
        await using var internalServer = new ModbusServerService(NullLogger<ModbusServerService>.Instance);
        await using var runtime = new ModbusRuntimeService(
            client,
            internalServer,
            new TestOptionsMonitor(options),
            NullLogger<ModbusRuntimeService>.Instance);
        await using var service = CreateService(runtime, client, internalServer, options);

        var start = await service.StartClientAsync();
        Assert.True(start.Succeeded, start.ErrorMessage);
        Assert.True(await WaitForAsync(() => service.State.ClientState == ModbusConnectionState.Running));

        var set = await service.SetAsync("DemoInput", (ushort)7);

        Assert.True(set.Succeeded, set.ErrorMessage);
        Assert.True(await WaitForAsync(
            () => externalServer.Snapshot.HoldingRegisters.Count > 1
                && externalServer.Snapshot.HoldingRegisters[1] == 7));
    }

    [Fact]
    public async Task BothMode_ClientWritesUpdateLocalServerSnapshot()
    {
        var port = GetAvailablePort();
        var options = CreateOptions(port);
        options.WriteConfirmationTimeoutMs = 2000;
        await using var client = new ModbusClientService(NullLogger<ModbusClientService>.Instance);
        await using var server = new ModbusServerService(NullLogger<ModbusServerService>.Instance);
        await using var runtime = new ModbusRuntimeService(
            client,
            server,
            new TestOptionsMonitor(options),
            NullLogger<ModbusRuntimeService>.Instance);
        await using var service = CreateService(runtime, client, server, options);

        await runtime.StartServerAsync(options);
        Assert.True(await WaitForAsync(() => service.State.ServerState == ModbusConnectionState.Running));

        await runtime.StartClientAsync(options);
        Assert.True(await WaitForAsync(() => service.State.ActiveRole == ModbusRunMode.Both));
        Assert.True(await WaitForAsync(() => service.State.ClientState == ModbusConnectionState.Running));

        var coilResult = await service.SetAsync("DemoButton", true);
        var registerResult = await service.SetAsync("DemoInput", (ushort)9);

        Assert.True(coilResult.Succeeded, coilResult.ErrorMessage);
        Assert.True(registerResult.Succeeded, registerResult.ErrorMessage);
        Assert.True(await WaitForAsync(
            () => server.Snapshot.Coils.Count > 0
                && server.Snapshot.Coils[0]));
        Assert.True(await WaitForAsync(
            () => server.Snapshot.HoldingRegisters.Count > 1
                && server.Snapshot.HoldingRegisters[1] == 9));
    }

    [Fact]
    public async Task HoldingRegisterBitWrite_PreservesOtherBitsFromSnapshot()
    {
        var options = CreateOptions();
        options.WriteConfirmationTimeoutMs = 0;
        options.DataMap.Add(CreateRegisterBit("Bit0", 2, 0));
        var runtime = new FakeRuntimeService();
        var client = new NoopClientService();
        await using var service = CreateService(runtime, client, new NoopServerService(), options);
        await service.StartClientAsync();
        runtime.PublishSnapshot([0, 0, 2]);

        var result = await service.SetAsync("Bit0", true);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal((2, (ushort)3), Assert.Single(client.RegisterWrites));
    }

    [Fact]
    public async Task ConcurrentHoldingRegisterBitWrites_AreSerializedWithoutLostBits()
    {
        var options = CreateOptions();
        options.WriteConfirmationTimeoutMs = 0;
        options.DataMap.Add(CreateRegisterBit("Bit0", 2, 0));
        options.DataMap.Add(CreateRegisterBit("Bit1", 2, 1));
        var runtime = new FakeRuntimeService();
        var client = new NoopClientService();
        await using var service = CreateService(runtime, client, new NoopServerService(), options);
        await service.StartClientAsync();
        runtime.PublishSnapshot([0, 0, 0]);

        var results = await Task.WhenAll(
            service.SetAsync("Bit0", true),
            service.SetAsync("Bit1", true));

        Assert.All(results, result => Assert.True(result.Succeeded, result.ErrorMessage));
        Assert.Equal((ushort)3, client.RegisterWrites[^1].Value);
    }

    [Fact]
    public async Task DataSnapshot_IsPublishedEveryPollWhilePointSubscriptionRemainsChangeOnly()
    {
        var options = CreateOptions();
        var runtime = new FakeRuntimeService();
        await using var service = CreateService(runtime, new NoopClientService(), new NoopServerService(), options);
        await service.StartClientAsync();
        var snapshots = 0;
        var pointChanges = 0;
        service.SnapshotChanged += (_, _) => snapshots++;
        using var subscription = service.Subscribe("DemoInput", _ => pointChanges++);

        runtime.PublishSnapshot([0, 7]);
        runtime.PublishSnapshot([0, 7]);

        Assert.Equal(2, snapshots);
        Assert.Equal(1, pointChanges);
        Assert.Equal((ushort)7, service.CurrentSnapshot.Values["DemoInput"].Value);
    }

    [Fact]
    public async Task ApplyDataMap_ReplacesLookupWithoutRestartAndWaitsForNextSnapshot()
    {
        var options = CreateOptions();
        var runtime = new FakeRuntimeService();
        await using var service = CreateService(runtime, new NoopClientService(), new NoopServerService(), options);
        await service.StartClientAsync();
        runtime.PublishSnapshot([0, 7, 0]);
        var dataMapRuntime = (IModbusDataMapRuntime)service;
        var replacement = new ModbusDataPointOptions
        {
            Name = "Replacement",
            Area = ModbusDataArea.HoldingRegister,
            Address = 2,
            Length = 1,
            Type = ModbusValueType.UInt16,
            Access = ModbusDataAccess.Read
        };

        var apply = dataMapRuntime.ApplyDataMap([replacement]);
        var oldPoint = await service.GetAsync<ushort>("DemoInput");
        var beforePoll = await service.GetAsync<ushort>("Replacement");
        runtime.PublishSnapshot([0, 7, 9]);
        var afterPoll = await service.GetAsync<ushort>("Replacement");

        Assert.True(apply.Succeeded, apply.ErrorMessage);
        Assert.Equal("ModbusDataPointMissing", oldPoint.ErrorCode);
        Assert.Equal("ModbusValueUnavailable", beforePoll.ErrorCode);
        Assert.True(afterPoll.Succeeded, afterPoll.ErrorMessage);
        Assert.Equal((ushort)9, afterPoll.Value);
    }

    private static ModbusTcpService CreateService(
        IModbusRuntimeService runtime,
        IModbusClientService client,
        IModbusServerService server,
        ModbusOptions options,
        ICommandAuditService? commandAuditService = null)
        => new(
            runtime,
            client,
            server,
            new TestOptionsMonitor(options),
            new ModbusDataMapValidator(),
            CreatePhysicalWriteAuditSink(commandAuditService),
            NullLogger<ModbusTcpService>.Instance);

    private static ModbusPhysicalWriteAuditSink CreatePhysicalWriteAuditSink(
        ICommandAuditService? commandAuditService = null)
        => new(
            commandAuditService ?? new NoopCommandAuditService(),
            new TestArchiveOptionsMonitor(new ArchiveOptions()),
            NullLogger<ModbusPhysicalWriteAuditSink>.Instance);

    private static CommandExecutionContext CreateCommandContext(
        string signalId,
        SignalValueType valueType,
        string requestedValueCanonical,
        ModbusWriteMode writeMode)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            sessionId: null,
            userId: null,
            username: null,
            "device-1",
            signalId,
            valueType,
            requestedValueCanonical,
            writeMode,
            isEmergency: false,
            schemaVersion: 1);

    private static ModbusOptions CreateOptions(int? port = null)
    {
        var selectedPort = port ?? GetAvailablePort();
        return new ModbusOptions
        {
            WriteConfirmationTimeoutMs = 500,
            Client = new ModbusEndpointOptions
            {
                Host = "127.0.0.1",
                Port = selectedPort,
                UnitId = 1,
                PollIntervalMs = 100,
                CoilCount = 4,
                RegisterCount = 4,
                CoilsEnabled = true,
                HoldingRegistersEnabled = true
            },
            Server = new ModbusEndpointOptions
            {
                Enabled = true,
                Port = selectedPort,
                UnitId = 1,
                PollIntervalMs = 100,
                CoilCount = 4,
                RegisterCount = 4
            },
            DataMap =
            [
                new()
                {
                    Name = "DemoButton",
                    Area = ModbusDataArea.Coil,
                    Address = 0,
                    Type = ModbusValueType.Bool,
                    Access = ModbusDataAccess.ReadWrite
                },
                new()
                {
                    Name = "DemoInput",
                    Area = ModbusDataArea.HoldingRegister,
                    Address = 1,
                    Type = ModbusValueType.UInt16,
                    Access = ModbusDataAccess.ReadWrite
                }
            ]
        };
    }

    private static ModbusDataPointOptions CreateRegisterBit(string name, int address, int bitIndex)
        => new()
        {
            Name = name,
            Area = ModbusDataArea.HoldingRegister,
            Address = address,
            Length = 1,
            BitIndex = bitIndex,
            Type = ModbusValueType.Bool,
            Access = ModbusDataAccess.ReadWrite
        };

    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        var startedAt = DateTimeOffset.UtcNow;

        while (DateTimeOffset.UtcNow - startedAt < TimeSpan.FromSeconds(5))
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class TestOptionsMonitor(ModbusOptions currentValue) : IOptionsMonitor<ModbusOptions>
    {
        public ModbusOptions CurrentValue { get; } = currentValue;

        public ModbusOptions Get(string? name)
            => CurrentValue;

        public IDisposable? OnChange(Action<ModbusOptions, string?> listener)
            => null;
    }

    private sealed class TestArchiveOptionsMonitor(ArchiveOptions currentValue) : IOptionsMonitor<ArchiveOptions>
    {
        public ArchiveOptions CurrentValue { get; } = currentValue;

        public ArchiveOptions Get(string? name)
            => CurrentValue;

        public IDisposable? OnChange(Action<ArchiveOptions, string?> listener)
            => null;
    }

    private sealed class RecordingCommandAuditService : ICommandAuditService
    {
        public List<PhysicalModbusWriteAuditRecord> PhysicalWrites { get; } = [];

        public Task<ArchiveOperationResult> RecordCommandAsync(
            EquipmentCommandAuditRecord record,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ArchiveOperationResult.Success());

        public Task<ArchiveOperationResult> RecordPhysicalWriteAsync(
            PhysicalModbusWriteAuditRecord record,
            CancellationToken cancellationToken = default)
        {
            PhysicalWrites.Add(record);
            return Task.FromResult(ArchiveOperationResult.Success());
        }
    }

    private sealed class FakeRuntimeService : IModbusRuntimeService
    {
        public List<string> Calls { get; } = [];
        public ModbusStatus Status { get; private set; } = ModbusStatus.Stopped;
        public ModbusSnapshot ClientSnapshot { get; private set; } = ModbusSnapshot.Empty;
        public ModbusSnapshot ServerSnapshot { get; private set; } = ModbusSnapshot.Empty;
        public ModbusOptions CurrentOptions { get; private set; } = new();
        public event EventHandler<ModbusStatus>? StatusChanged;
        public event EventHandler<ModbusSnapshot>? SnapshotChanged;

        public Task StartAsync(ModbusRunMode mode = ModbusRunMode.Both, ModbusOptions? options = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("Stop");
            Status = ModbusStatus.Stopped;
            StatusChanged?.Invoke(this, Status);
            return Task.CompletedTask;
        }

        public Task RestartAsync(ModbusRunMode mode = ModbusRunMode.Both, ModbusOptions? options = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void PublishRunningRoles()
        {
            Status = new ModbusStatus
            {
                ClientState = ModbusConnectionState.Running,
                ServerState = ModbusConnectionState.Running,
                ClientMessage = "Client running",
                ServerMessage = "Server running"
            };
            StatusChanged?.Invoke(this, Status);
        }

        public void PublishSnapshot(IReadOnlyList<ushort> holdingRegisters)
        {
            ClientSnapshot = new ModbusSnapshot
            {
                Role = ModbusRuntimeRole.Client,
                Coils = [],
                HoldingRegisters = holdingRegisters,
                DecodedRegisters = ModbusDecodedRegisters.Empty,
                Timestamp = DateTimeOffset.Now
            };
            SnapshotChanged?.Invoke(this, ClientSnapshot);
        }

        public Task StartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add("StartClient");
            CurrentOptions = options?.Clone() ?? CurrentOptions;
            Status = new ModbusStatus
            {
                ClientState = ModbusConnectionState.Running,
                ServerState = ModbusConnectionState.Stopped,
                ClientMessage = "Client running"
            };
            StatusChanged?.Invoke(this, Status);
            return Task.CompletedTask;
        }

        public Task StopClientAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("StopClient");
            Status = Status.WithClientState(ModbusConnectionState.Stopped);
            StatusChanged?.Invoke(this, Status);
            return Task.CompletedTask;
        }

        public Task RestartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add("StartServer");
            CurrentOptions = options?.Clone() ?? CurrentOptions;
            Status = new ModbusStatus
            {
                ClientState = ModbusConnectionState.Stopped,
                ServerState = ModbusConnectionState.Running,
                ServerMessage = "Server running"
            };
            StatusChanged?.Invoke(this, Status);
            return Task.CompletedTask;
        }

        public Task StopServerAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("StopServer");
            Status = Status.WithServerState(ModbusConnectionState.Stopped);
            StatusChanged?.Invoke(this, Status);
            return Task.CompletedTask;
        }

        public Task RestartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

    private sealed class NoopClientService : IModbusClientService
    {
        public ModbusConnectionState State { get; } = ModbusConnectionState.Running;
        public ModbusSnapshot Snapshot { get; } = ModbusSnapshot.Empty;
        public List<(int Address, bool Value)> CoilWrites { get; } = [];
        public List<(int Address, ushort Value)> RegisterWrites { get; } = [];
        public List<(int StartAddress, ushort[] Values)> RegistersWrites { get; } = [];
        public event EventHandler<ModbusStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<ModbusSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

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

        public Task WriteRegistersAsync(int startAddress, ushort[] values, CancellationToken cancellationToken = default)
        {
            RegistersWrites.Add((startAddress, values));
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoopServerService : IModbusServerService
    {
        public ModbusConnectionState State { get; } = ModbusConnectionState.Running;
        public ModbusSnapshot Snapshot { get; } = ModbusSnapshot.Empty;
        public List<(int Address, bool Value)> CoilWrites { get; } = [];
        public List<(int Address, ushort Value)> RegisterWrites { get; } = [];
        public List<(int StartAddress, ushort[] Values)> RegistersWrites { get; } = [];
        public event EventHandler<ModbusStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<ModbusSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

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

        public Task SetRegistersAsync(int startAddress, ushort[] values, CancellationToken cancellationToken = default)
        {
            RegistersWrites.Add((startAddress, values));
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal static class ModbusStatusTestExtensions
{
    public static ModbusStatus WithClientState(this ModbusStatus status, ModbusConnectionState state)
        => new()
        {
            ClientState = state,
            ServerState = status.ServerState,
            ClientMessage = status.ClientMessage,
            ServerMessage = status.ServerMessage,
            LastError = status.LastError,
            UpdatedAt = DateTimeOffset.Now
        };

    public static ModbusStatus WithServerState(this ModbusStatus status, ModbusConnectionState state)
        => new()
        {
            ClientState = status.ClientState,
            ServerState = state,
            ClientMessage = status.ClientMessage,
            ServerMessage = status.ServerMessage,
            LastError = status.LastError,
            UpdatedAt = DateTimeOffset.Now
        };
}
