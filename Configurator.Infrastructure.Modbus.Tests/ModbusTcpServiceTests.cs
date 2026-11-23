using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
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

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed partial class ModbusTcpServiceTests
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
    public async Task SetAsync_PulseTrueAcceptsTransportWithoutReadbackButFalseStillRequiresConfirmation()
    {
        var options = CreateOptions();
        options.WriteConfirmationTimeoutMs = 50;
        options.DataMap.Single(point => point.Name == "DemoButton").WriteMode = ModbusWriteMode.Pulse;
        var client = new NoopClientService();
        await using var service = CreateService(new FakeRuntimeService(), client, new NoopServerService(), options);
        await service.StartClientAsync();

        var set = await service.SetAsync("DemoButton", true);
        var reset = await service.SetAsync("DemoButton", false);

        Assert.True(set.Succeeded, set.ErrorMessage);
        Assert.False(reset.Succeeded);
        Assert.Equal("ModbusWriteConfirmationTimeout", reset.ErrorCode);
        Assert.Equal([(0, true), (0, false)], client.CoilWrites);
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
    public async Task Dispatcher_PulsesThreeTopBarRegisterBitsIndependentlyAndPreservesTheirNeighbors()
    {
        var options = CreateOptions();
        options.WriteConfirmationTimeoutMs = 0;
        var points = new[]
        {
            CreateRegisterBit("system.command.unload", 0, 0),
            CreateRegisterBit("system.command.rotate", 2, 1),
            CreateRegisterBit("system.command.load", 3, 2),
        };
        foreach (var point in points)
        {
            point.WriteMode = ModbusWriteMode.Pulse;
            point.PulseDurationMs = 25;
            options.DataMap.Add(point);
        }

        var runtime = new FakeRuntimeService();
        var client = new NoopClientService();
        await using var service = CreateService(runtime, client, new NoopServerService(), options);
        Assert.True((await service.StartClientAsync()).Succeeded);
        runtime.PublishSnapshot([0b1010_0000, 0, 0b0100_0000, 0b0001_0000]);
        var dispatcher = new ModbusTcpCommandDispatcher(service, new TestOptionsMonitor(options));

        foreach (var point in points)
            await dispatcher.DispatchAsync(new SignalWriteRequest(point.Name, true, SignalValueType.Bool)
                { RequiredWriteMode = ModbusWriteMode.Pulse });

        Assert.Equal(new (int Address, ushort Value)[]
        {
            (0, 0b1010_0001), (0, 0b1010_0000),
            (2, 0b0100_0010), (2, 0b0100_0000),
            (3, 0b0001_0100), (3, 0b0001_0000),
        }, client.RegisterWrites);
    }

    [Fact]
    public async Task Dispatcher_RejectsSavedPulseWhenTheActivePointIsLatched()
    {
        var options = CreateOptions();
        var client = new NoopClientService();
        await using var service = CreateService(new FakeRuntimeService(), client, new NoopServerService(), options);
        Assert.True((await service.StartClientAsync()).Succeeded);
        var savedOptions = options.Clone();
        savedOptions.DataMap[0].WriteMode = ModbusWriteMode.Pulse;
        var dispatcher = new ModbusTcpCommandDispatcher(service, new TestOptionsMonitor(savedOptions));

        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(
            new SignalWriteRequest("DemoButton", true, SignalValueType.Bool) { RequiredWriteMode = ModbusWriteMode.Pulse }));

        Assert.Contains("differs from Modbus.DataMap", error.Message);
        Assert.Empty(client.CoilWrites);
    }

    [Fact]
    public async Task Dispatcher_DoesNotResetAReplacementPointAfterTheRisingEdge()
    {
        var options = CreateOptions();
        options.DataMap[0].WriteMode = ModbusWriteMode.Pulse;
        options.DataMap[0].PulseDurationMs = 20;
        var client = new NoopClientService();
        await using var service = CreateService(new FakeRuntimeService(), client, new NoopServerService(), options);
        Assert.True((await service.StartClientAsync()).Succeeded);
        var replacement = options.Clone();
        replacement.DataMap[0].Address = 3;
        ModbusOperationResult? applyResult = null;
        client.OnCoilWrite = () => applyResult = service.ApplyDataMap(replacement.DataMap);
        var dispatcher = new ModbusTcpCommandDispatcher(service, new TestOptionsMonitor(options));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(
            new SignalWriteRequest("DemoButton", true, SignalValueType.Bool) { RequiredWriteMode = ModbusWriteMode.Pulse }));

        Assert.True(applyResult?.Succeeded);
        Assert.Contains("PLC command state is unknown", error.Message);
        Assert.Contains("differs from Modbus.DataMap", error.InnerException?.Message);
        Assert.Equal((0, true), Assert.Single(client.CoilWrites));
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
    public async Task RemovePointWhileStoppedWaitsForPersistenceAndRejectsActiveRole()
    {
        var options = CreateOptions(port: 1502);
        var monitor = new TestOptionsMonitor(options);
        var client = new NoopClientService { State = ModbusConnectionState.Running };
        var server = new NoopServerService { State = ModbusConnectionState.Stopped };
        await using var runtime = new ModbusRuntimeService(client, server, monitor, NullLogger<ModbusRuntimeService>.Instance);
        await using var service = CreateService(runtime, client, server, options);

        var refused = await service.RemovePointWhileStoppedAsync("DemoInput", _ =>
            throw new InvalidOperationException("Persistence must not run while a role is active."));
        Assert.Equal("ModbusMustBeStopped", refused.ErrorCode);
        client.State = ModbusConnectionState.Stopped;
        var failedWrite = await Assert.ThrowsAsync<IOException>(() =>
            service.RemovePointWhileStoppedAsync("DemoInput", _ => throw new IOException()));
        Assert.NotNull(failedWrite);
        Assert.NotEqual("ModbusDataPointMissing", (await service.GetAsync<ushort>("DemoInput")).ErrorCode);

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deletion = service.RemovePointWhileStoppedAsync("DemoInput", async _ =>
        {
            entered.SetResult();
            await allowSave.Task;
            options.DataMap.RemoveAll(point => point.Name == "DemoInput");
            return true;
        });
        await entered.Task;
        var start = runtime.StartAsync(ModbusRunMode.Client);
        Assert.False(start.IsCompleted);
        allowSave.SetResult();

        Assert.True((await deletion).Succeeded);
        await start;
        Assert.Equal("ModbusDataPointMissing", (await service.GetAsync<ushort>("DemoInput")).ErrorCode);
        Assert.False(service.CurrentSnapshot.Values.ContainsKey("DemoInput"));
        Assert.Empty(client.RegisterWrites);
        Assert.Empty(server.RegisterWrites);
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

    [Fact]
    public async Task BucketFrequency_ReadsTheRawUnsignedWordAtOffsetTen()
    {
        var options = CreateOptions();
        options.Client.RegisterCount = 13;
        options.DataMap =
        [
            new ModbusDataPointOptions { Name = "equip.bucket.frequency", Address = 10 }
        ];
        var runtime = new FakeRuntimeService();
        await using var service = CreateService(runtime, new NoopClientService(), new NoopServerService(), options);
        Assert.True((await service.StartClientAsync()).Succeeded);
        var registers = new ushort[13];
        registers[9] = 9;
        registers[10] = 0xF234;
        registers[11] = 11;
        runtime.PublishSnapshot(registers);

        var sample = await service.GetAsync<ushort>("equip.bucket.frequency");

        Assert.True(sample.Succeeded, sample.ErrorMessage);
        Assert.Equal((ushort)0xF234, sample.Value);
    }

    private static ModbusTcpService CreateService(
        IModbusRuntimeService runtime,
        IModbusClientService client,
        IModbusServerService server,
        ModbusOptions options)
        => new(
            runtime,
            client,
            server,
            new TestOptionsMonitor(options),
            new ModbusDataMapValidator(),
            NullLogger<ModbusTcpService>.Instance);

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

        public void PublishRawSnapshot(ModbusSnapshot snapshot)
        {
            if (snapshot.Role == ModbusRuntimeRole.Client)
                ClientSnapshot = snapshot;
            else
                ServerSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }

        public void ChangeEndpoint()
        {
            var next = CurrentOptions.Clone();
            next.Client.Port++;
            CurrentOptions = next;
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
        public ModbusConnectionState State { get; set; } = ModbusConnectionState.Running;
        public ModbusSnapshot Snapshot { get; } = ModbusSnapshot.Empty;
        public List<(int Address, bool Value)> CoilWrites { get; } = [];
        public Action? OnCoilWrite { get; set; }
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
            OnCoilWrite?.Invoke();
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
        public ModbusConnectionState State { get; set; } = ModbusConnectionState.Running;
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
