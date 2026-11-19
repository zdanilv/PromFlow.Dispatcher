using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Infrastructure.Modbus.Runtime;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed partial class ModbusTcpServiceTests
{
    [Fact]
    public async Task Confirmation_PreWriteEqualValueWithoutNewReadTimesOut()
    {
        await using var f = await ConfirmationFixture.StartAsync(timeout: 80);
        f.Publish();
        var result = await f.Service.SetAsync("DemoInput", (ushort)5);
        Assert.False(result.Succeeded);
        Assert.Equal("ModbusWriteConfirmationTimeout", result.ErrorCode);
        Assert.Single(f.Client.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Confirmation_EqualNewReadConfirmsWithoutValueChangeNotification(bool server)
    {
        await using var f = await ConfirmationFixture.StartAsync(server: server);
        f.Publish();
        var callbacks = 0;
        var snapshots = 0;
        using var subscription = f.Service.Subscribe("DemoInput", _ => callbacks++);
        f.Service.SnapshotChanged += (_, _) => snapshots++;
        var operation = f.Service.SetAsync("DemoInput", (ushort)5);
        var receipt = await f.Writer.Written.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(operation.IsCompleted);
        var read = f.Publish();
        Assert.True(read.HoldingRegistersReadState!.SuccessfulReadSequence > receipt.Sequence);
        Assert.True((await operation).Succeeded);
        Assert.Equal(1, callbacks);
        Assert.Equal(1, snapshots);
        Assert.Single(f.Writer.Writes);
    }

    [Fact]
    public async Task Confirmation_MismatchingNewReadKeepsWaitingForMatchingRead()
    {
        await using var f = await ConfirmationFixture.StartAsync();
        f.Publish();
        var operation = f.Service.SetAsync("DemoInput", (ushort)5);
        await f.Client.Written.Task.WaitAsync(TimeSpan.FromSeconds(2));
        f.Publish(register: 6);
        await Task.WhenAny(operation, Task.Delay(75));
        Assert.False(operation.IsCompleted);
        f.Publish(register: 5);
        Assert.True((await operation).Succeeded);
        Assert.Single(f.Client.Writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Confirmation_FailedTargetAreaCannotBeConfirmedByHealthyPeer(bool coil)
    {
        await using var f = await ConfirmationFixture.StartAsync(timeout: 100);
        var before = f.Publish();
        var operation = coil ? f.Service.SetAsync("DemoButton", false) : f.Service.SetAsync("DemoInput", (ushort)5);
        await f.Client.Written.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var read = f.Read();
        read = CopyRead(read, coil ? before.CoilsReadState! with { LastAttemptSucceeded = false } : read.CoilsReadState!,
            coil ? read.HoldingRegistersReadState! : before.HoldingRegistersReadState! with { LastAttemptSucceeded = false });
        f.Runtime.PublishRawSnapshot(read);
        var result = await operation;
        Assert.False(result.Succeeded);
        Assert.Equal("ModbusWriteConfirmationTimeout", result.ErrorCode);
        Assert.Single(f.Client.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Confirmation_RoleSwitchInvalidatesPendingWrite(bool startWithServer)
    {
        await using var f = await ConfirmationFixture.StartAsync(server: startWithServer);
        f.Publish();
        var operation = f.Service.SetAsync("DemoInput", (ushort)5);
        await f.Writer.Written.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (startWithServer) await f.Service.StartClientAsync();
        else await f.Service.StartServerAsync();
        f.Runtime.PublishRawSnapshot(f.Read(server: !startWithServer));
        var result = await operation;
        Assert.False(result.Succeeded);
        Assert.Equal("ModbusWriteConfirmationContextChanged", result.ErrorCode);
        Assert.Single(f.Writer.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Confirmation_MapOrEndpointChangeInvalidatesPendingWrite(bool endpoint)
    {
        await using var f = await ConfirmationFixture.StartAsync();
        f.Publish();
        var operation = f.Service.SetAsync("DemoInput", (ushort)5);
        await f.Client.Written.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (endpoint) f.Runtime.ChangeEndpoint();
        else
        {
            var map = f.Options.DataMap.Select(p => p.Clone()).ToArray();
            map.Single(p => p.Name == "DemoInput").Address = 2;
            Assert.True(f.Service.ApplyDataMap(map).Succeeded);
        }
        f.Publish();
        var result = await operation;
        Assert.False(result.Succeeded);
        Assert.Equal("ModbusWriteConfirmationContextChanged", result.ErrorCode);
        Assert.Single(f.Client.Writes);
    }

    [Fact]
    public async Task Confirmation_PulseReleaseDoesNotUseCachedFalseAndRisingNeedsNoRead()
    {
        await using var f = await ConfirmationFixture.StartAsync(timeout: 80, pulse: true);
        f.Publish();
        Assert.True((await f.Service.SetAsync("DemoButton", true)).Succeeded);
        var release = await f.Service.SetAsync("DemoButton", false);
        Assert.False(release.Succeeded);
        Assert.Equal("ModbusWriteConfirmationTimeout", release.ErrorCode);
        Assert.Equal([true, false], f.Client.Writes.Select(w => (bool)w.Value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Confirmation_ZeroTimeoutAndWriteOnlyDoNotRequireRead(bool writeOnly)
    {
        await using var f = await ConfirmationFixture.StartAsync(timeout: writeOnly ? 100 : 0, writeOnly: writeOnly);
        var result = await f.Service.SetAsync("DemoInput", (ushort)5);
        Assert.True(result.Succeeded);
        Assert.Single(f.Client.Writes);
    }

    [Fact]
    public async Task Confirmation_ReadArrivingAfterTimeoutCannotChangeFinishedFailure()
    {
        await using var f = await ConfirmationFixture.StartAsync(timeout: 60);
        f.Publish();
        var operation = f.Service.SetAsync("DemoInput", (ushort)5);
        var result = await operation;
        Assert.Equal("ModbusWriteConfirmationTimeout", result.ErrorCode);
        f.Publish();
        Assert.Same(result, await operation);
        Assert.False(result.Succeeded);
        Assert.Single(f.Client.Writes);
    }

    [Fact]
    public async Task Confirmation_DelayedPreWriteSnapshotCannotConfirmEvenIfDeliveredAfterSend()
    {
        await using var f = await ConfirmationFixture.StartAsync(timeout: 120);
        f.Publish();
        var delayed = f.Read();
        var operation = f.Service.SetAsync("DemoInput", (ushort)5);
        var receipt = await f.Client.Written.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(delayed.HoldingRegistersReadState!.SuccessfulReadSequence < receipt.Sequence);
        f.Runtime.PublishRawSnapshot(delayed);
        Assert.Equal("ModbusWriteConfirmationTimeout", (await operation).ErrorCode);
    }

    [Fact]
    public async Task Confirmation_ReadAcquiredWhileWriteWaitsForIoCannotConfirm()
    {
        await using var f = await ConfirmationFixture.StartAsync();
        f.Publish();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Client.BeforeSend = async () => { entered.SetResult(); await release.Task; };
        var operation = f.Service.SetAsync("DemoInput", (ushort)5);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var delayed = f.Read();
        release.SetResult();
        var receipt = await f.Client.Written.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(delayed.HoldingRegistersReadState!.SuccessfulReadSequence < receipt.Sequence);
        f.Runtime.PublishRawSnapshot(delayed);
        await Task.WhenAny(operation, Task.Delay(75));
        Assert.False(operation.IsCompleted);
        f.Publish();
        Assert.True((await operation).Succeeded);
        Assert.Single(f.Client.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Confirmation_BothRejectsWrongRoleAndChangedSession(bool wrongRole)
    {
        await using var f = await ConfirmationFixture.StartAsync(timeout: 120);
        f.Runtime.PublishRunningRoles();
        f.Publish();
        var operation = f.Service.SetAsync("DemoInput", (ushort)5);
        await f.Client.Written.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var read = f.Read(server: wrongRole, session: wrongRole ? null : Guid.NewGuid());
        f.Runtime.PublishRawSnapshot(read);
        Assert.False((await operation).Succeeded);
        Assert.Single(f.Client.Writes);
        Assert.Empty(f.Server.Writes);
    }

    [Fact]
    public async Task Confirmation_SynchronousReadBeforeTransportReturnsCanConfirm()
    {
        await using var f = await ConfirmationFixture.StartAsync();
        f.Publish();
        f.Client.AfterSend = () => f.Publish();
        Assert.True((await f.Service.SetAsync("DemoInput", (ushort)5)).Succeeded);
        Assert.Single(f.Client.Writes);
    }

    [Fact]
    public async Task Confirmation_SessionChangesCannotBeHiddenByReturnToOriginalSession()
    {
        await using var f = await ConfirmationFixture.StartAsync();
        f.Publish();
        f.Client.AfterSend = () =>
        {
            f.Runtime.PublishRawSnapshot(f.Read(session: Guid.NewGuid()));
            f.Publish();
        };
        var result = await f.Service.SetAsync("DemoInput", (ushort)5);
        Assert.Equal("ModbusWriteConfirmationContextChanged", result.ErrorCode);
        Assert.Single(f.Client.Writes);
    }

    [Fact]
    public async Task Confirmation_WriteBeforeFirstSnapshotCanUseFirstPostWriteRead()
    {
        await using var f = await ConfirmationFixture.StartAsync();
        f.Client.AfterSend = () => f.Publish();
        Assert.True((await f.Service.SetAsync("DemoInput", (ushort)5)).Succeeded);
        Assert.Single(f.Client.Writes);
    }

    [Fact]
    public async Task Confirmation_PulseReleaseAcceptsFreshFalseAfterSend()
    {
        await using var f = await ConfirmationFixture.StartAsync(pulse: true);
        f.Publish();
        Assert.True((await f.Service.SetAsync("DemoButton", true)).Succeeded);
        f.Client.AfterSend = () => f.Publish();
        Assert.True((await f.Service.SetAsync("DemoButton", false)).Succeeded);
        Assert.Equal([true, false], f.Client.Writes.Select(w => (bool)w.Value));
    }

    private static ModbusSnapshot CopyRead(ModbusSnapshot source, ModbusAreaReadState coils, ModbusAreaReadState registers) => new()
    {
        Role = source.Role, SourceSessionId = source.SourceSessionId, ReadSequence = source.ReadSequence,
        Coils = source.Coils, HoldingRegisters = source.HoldingRegisters, Timestamp = source.Timestamp,
        CoilsReadState = coils, HoldingRegistersReadState = registers
    };

    private sealed class ConfirmationFixture : IAsyncDisposable
    {
        public ModbusOptions Options { get; } = CreateOptions();
        public FakeRuntimeService Runtime { get; } = new();
        public ConfirmationTransport Client { get; } = new(ModbusRuntimeRole.Client);
        public ConfirmationTransport Server { get; } = new(ModbusRuntimeRole.Server);
        public ModbusTcpService Service { get; private set; } = null!;
        private bool _server;
        public ConfirmationTransport Writer => _server ? Server : Client;

        public static async Task<ConfirmationFixture> StartAsync(int timeout = 500, bool server = false, bool pulse = false, bool writeOnly = false)
        {
            var f = new ConfirmationFixture { _server = server };
            f.Options.WriteConfirmationTimeoutMs = timeout;
            if (pulse) f.Options.DataMap.Single(p => p.Name == "DemoButton").WriteMode = ModbusWriteMode.Pulse;
            if (writeOnly) f.Options.DataMap.Single(p => p.Name == "DemoInput").Access = ModbusDataAccess.Write;
            f.Service = CreateService(f.Runtime, f.Client, f.Server, f.Options);
            if (server) await f.Service.StartServerAsync();
            else await f.Service.StartClientAsync();
            return f;
        }

        public ModbusSnapshot Read(ushort register = 5, bool? server = null, Guid? session = null)
        {
            var useServer = server ?? _server;
            return QualitySnapshot(useServer ? ModbusRuntimeRole.Server : ModbusRuntimeRole.Client,
                [0, register, register], session ?? (useServer ? Server.SessionId : Client.SessionId));
        }
        public ModbusSnapshot Publish(ushort register = 5)
        {
            var read = Read(register);
            Runtime.PublishRawSnapshot(read);
            return read;
        }
        public ValueTask DisposeAsync() => Service.DisposeAsync();
    }

    private sealed class ConfirmationTransport(ModbusRuntimeRole role) : IModbusClientService, IModbusServerService, IModbusObservedWriter
    {
        public Guid SessionId { get; } = Guid.NewGuid();
        public ModbusConnectionState State => ModbusConnectionState.Running;
        public ModbusSnapshot Snapshot => ModbusSnapshot.Empty;
        public List<(ModbusDataArea Area, int Address, object Value)> Writes { get; } = [];
        public TaskCompletionSource<ModbusWriteReceipt> Written { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<Task>? BeforeSend { get; set; }
        public Action? AfterSend { get; set; }
        public event EventHandler<ModbusStatus>? StatusChanged { add { } remove { } }
        public event EventHandler<ModbusSnapshot>? SnapshotChanged { add { } remove { } }
        public async Task<ModbusWriteReceipt> WriteObservedAsync(ModbusDataArea area, int address, bool coilValue, ushort[] registers, bool pulse, CancellationToken cancellationToken)
        {
            if (BeforeSend is not null) await BeforeSend();
            var receipt = new ModbusWriteReceipt(role, SessionId, ModbusObservationSequence.Next());
            Writes.Add((area, address, area == ModbusDataArea.Coil ? coilValue : registers[0]));
            Written.TrySetResult(receipt);
            AfterSend?.Invoke();
            return receipt;
        }
        public Task StartAsync(ModbusEndpointOptions options, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteCoilAsync(int address, bool value, CancellationToken cancellationToken = default) { Writes.Add((ModbusDataArea.Coil, address, value)); return Task.CompletedTask; }
        public Task WriteRegisterAsync(int address, ushort value, CancellationToken cancellationToken = default) { Writes.Add((ModbusDataArea.HoldingRegister, address, value)); return Task.CompletedTask; }
        public Task WriteRegistersAsync(int address, ushort[] values, CancellationToken cancellationToken = default) { Writes.Add((ModbusDataArea.HoldingRegister, address, values[0])); return Task.CompletedTask; }
        public Task SetCoilAsync(int address, bool value, CancellationToken cancellationToken = default) => WriteCoilAsync(address, value, cancellationToken);
        public Task SetRegisterAsync(int address, ushort value, CancellationToken cancellationToken = default) => WriteRegisterAsync(address, value, cancellationToken);
        public Task SetRegistersAsync(int address, ushort[] values, CancellationToken cancellationToken = default) => WriteRegistersAsync(address, values, cancellationToken);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
