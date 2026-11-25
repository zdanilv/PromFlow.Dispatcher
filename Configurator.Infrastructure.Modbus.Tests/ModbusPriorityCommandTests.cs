using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Signals;
using Configurator.Infrastructure.Modbus.Client;
using Configurator.Infrastructure.Modbus.RouteMap;
using Configurator.Infrastructure.Modbus.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed partial class ModbusTcpServiceTests
{
    [Fact]
    public async Task PriorityGate_ServesPriorityFifoAndRemovesCancelledWaiter()
    {
        var gate = new ModbusPriorityGate();
        await gate.WaitAsync();
        var firstBackground = gate.WaitAsync();
        using var cancelled = new CancellationTokenSource();
        var discarded = gate.WaitAsync(cancelled.Token, priority: true);
        var firstCommand = gate.WaitAsync(priority: true);
        var secondCommand = gate.WaitAsync(priority: true);

        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => discarded);
        gate.Release();
        await firstCommand.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(secondCommand.IsCompleted);
        Assert.False(firstBackground.IsCompleted);
        gate.Release();
        await secondCommand.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(firstBackground.IsCompleted);
        gate.Release();
        await firstBackground.WaitAsync(TimeSpan.FromSeconds(2));
        gate.Release();
    }

    [Theory]
    [InlineData(ModbusRunMode.Server)]
    [InlineData(ModbusRunMode.Client)]
    [InlineData(ModbusRunMode.Both)]
    public async Task PriorityCommand_LoopbackFeedbackReleasesBeforeTimeoutAndDoesNotBlockStop(ModbusRunMode mode)
    {
        var options = CreateOptions();
        options.Server.BindAddress = "127.0.0.1";
        options.Client.Enabled = true;
        var command = CreateRegisterBit("equip.bucket.start", 0, 0);
        command.WriteMode = ModbusWriteMode.Pulse;
        command.PulseDurationMs = 8000;
        var stop = CreateRegisterBit("equip.bucket.stop", 0, 1);
        var reply = CreateRegisterBit("equip.bucket.start.reset", 2, 4);
        reply.Access = ModbusDataAccess.Read;
        options.DataMap = [command, stop, reply];

        await using var client = new ModbusClientService(NullLogger<ModbusClientService>.Instance);
        await using var server = new ModbusServerService(NullLogger<ModbusServerService>.Instance);
        await using var external = new ModbusServerService(NullLogger<ModbusServerService>.Instance);
        var monitor = new TestOptionsMonitor(options);
        await using var runtime = new ModbusRuntimeService(client, server, monitor, NullLogger<ModbusRuntimeService>.Instance);
        await using var service = CreateService(runtime, client, server, options);
        var dispatcher = new ModbusTcpCommandDispatcher(service, monitor);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var ct = deadline.Token;
        var plcStore = mode == ModbusRunMode.Client ? external : server;
        if (mode == ModbusRunMode.Client)
            await external.StartAsync(options.Server, ct);
        await runtime.StartAsync(mode, options, ct);
        await plcStore.SetRegisterAsync(0, 0x0040, ct);

        // In Server mode the simulated PLC exchanges its response over TCP.
        await using var plc = new ModbusClientService(NullLogger<ModbusClientService>.Instance);
        if (mode == ModbusRunMode.Server)
        {
            await plc.StartAsync(options.Client, ct);
            Assert.True(await WaitForAsync(() => plc.State == ModbusConnectionState.Running));
        }

        var writerRole = mode == ModbusRunMode.Server ? ModbusRuntimeRole.Server : ModbusRuntimeRole.Client;
        Assert.True(await WaitForAsync(() => service.CurrentSnapshot.SourceRole == writerRole
            && service.CurrentSnapshot.Values.TryGetValue(reply.Name, out var response) && response.Value is false));

        var request = new SignalWriteRequest(command.Name, true, SignalValueType.Bool)
        {
            RequiredWriteMode = ModbusWriteMode.Pulse,
            IsPriority = true,
            ResetFeedbackSignalId = reply.Name,
        };
        var start = dispatcher.DispatchAsync(request, ct);
        Assert.True(await WaitForAsync(() => RegistersEqual(0, 0x0041)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(request, ct));
        await dispatcher.DispatchAsync(new SignalWriteRequest(stop.Name, true, SignalValueType.Bool)
        {
            RequiredWriteMode = ModbusWriteMode.Latched,
            IsPriority = true,
        }, ct);
        Assert.True(await WaitForAsync(() => RegistersEqual(0, 0x0043)));
        Assert.False(start.IsCompleted);

        await WriteReplyAsync(0x0010);
        Assert.True(await WaitForAsync(() => RegistersEqual(0, 0x0042)));
        Assert.False(start.IsCompleted);
        await WriteReplyAsync(0);
        await start.WaitAsync(ct);
        Assert.True(RegistersEqual(0, 0x0042));

        bool RegistersEqual(int index, ushort value) =>
            plcStore.Snapshot.HoldingRegisters is { } registers && registers.Count > index && registers[index] == value;

        Task WriteReplyAsync(ushort value) => mode == ModbusRunMode.Server
            ? plc.WriteRegisterAsync(2, value, ct)
            : plcStore.SetRegisterAsync(2, value, ct);
    }
}
