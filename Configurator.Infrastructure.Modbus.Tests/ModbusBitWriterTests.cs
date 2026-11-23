using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Infrastructure.Modbus.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed class ModbusBitWriterTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PulseAsync_CoilWritesBothEdgesThroughTheSelectedRole(bool useClient)
    {
        var runtime = new FakeRuntime(useClient);
        var client = new RecordingEndpoint();
        var server = new RecordingEndpoint();

        var result = await CreateWriter(runtime, client, server).PulseAsync(Address(), 1);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var selected = useClient ? client : server;
        Assert.Equal(1, selected.Captures);
        Assert.Equal([new(ModbusDataArea.Coil, 2, null, true), new(ModbusDataArea.Coil, 2, null, false)], selected.Writes);
        Assert.Empty((useClient ? server : client).Writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PulseAsync_RejectsCollisionInTheActiveOrSavedMapAfterWaiting(bool activeMap)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runtime = new FakeRuntime(true);
        var client = new RecordingEndpoint();
        var server = new RecordingEndpoint();
        var options = new ModbusOptions();
        await using var dataMap = new ModbusTcpService(runtime, client, server,
            new StaticOptionsMonitor(options), new ModbusDataMapValidator(), NullLogger<ModbusTcpService>.Instance);
        var writer = new ModbusBitWriter(runtime, client, server, NullLogger<ModbusBitWriter>.Instance, dataMap: dataMap);
        var address = Address(ModbusDataArea.HoldingRegister);
        var mapChangeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var change = dataMap.RunDataMapChangeAsync(async () =>
        {
            mapChangeEntered.SetResult();
            await proceed.Task.WaitAsync(deadline.Token);
            ModbusDataPointOptions[] points =
            [
                new() { Name = "telemetry", Area = address.Area, Address = address.Address, Type = ModbusValueType.UInt16 }
            ];
            if (activeMap)
                Assert.True(dataMap.ApplyDataMap(points).Succeeded);
            else
                options.DataMap = [.. points];
            return true;
        }, deadline.Token);
        await mapChangeEntered.Task.WaitAsync(deadline.Token);

        var pulse = writer.PulseAsync(address, 1, deadline.Token);
        Assert.False(pulse.IsCompleted);
        proceed.SetResult();
        await change.WaitAsync(deadline.Token);

        AssertFailure(ModbusAlarmMapValidator.AcknowledgementDataMapConflict, await pulse.WaitAsync(deadline.Token));
        Assert.Equal(0, client.Captures);
        Assert.Empty(client.Attempts);
    }

    [Fact]
    public async Task PulseAsync_ReservesItsAddressUntilReleaseAndCanFinishDuringShutdown()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var hold = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var runtime = new FakeRuntime(true);
        var client = new RecordingEndpoint();
        var server = new RecordingEndpoint();
        var options = new ModbusOptions();
        await using var dataMap = new ModbusTcpService(runtime, client, server,
            new StaticOptionsMonitor(options), new ModbusDataMapValidator(), NullLogger<ModbusTcpService>.Instance);
        var writer = new ModbusBitWriter(runtime, client, server, NullLogger<ModbusBitWriter>.Instance, dataMap: dataMap);
        var address = Address(ModbusDataArea.HoldingRegister);

        var pulse = writer.PulseAsync(address, 60_000, hold.Token);
        Assert.True(Assert.Single(client.Writes).Value);
        options.DataMap =
        [
            new ModbusDataPointOptions { Name = "telemetry", Area = address.Area, Address = address.Address }
        ];
        AssertFailure(ModbusAlarmMapValidator.AcknowledgementDataMapConflict, dataMap.ApplyDataMap(options.DataMap));
        AssertFailure(ModbusAlarmMapValidator.AcknowledgementDataMapConflict,
            await dataMap.StartClientAsync(options, deadline.Token));
        await dataMap.DisposeAsync().AsTask().WaitAsync(deadline.Token);

        await hold.CancelAsync();
        AssertFailure("ModbusBitPulseCancelled", await pulse.WaitAsync(deadline.Token));
        Assert.Equal([true, false], client.Writes.Select(edge => edge.Value));
        AssertFailure("ModbusNotRunning", await writer.PulseAsync(address, 1, deadline.Token));
        Assert.Single(client.Writes, edge => edge.Value);
    }

    [Theory]
    [InlineData(ModbusDataArea.Coil)]
    [InlineData(ModbusDataArea.HoldingRegister)]
    public async Task PulseAsync_ReportsAndLogsFailedRelease(ModbusDataArea area)
    {
        var client = new RecordingEndpoint
        {
            BeforeWrite = (value, _) => value ? Task.CompletedTask : throw new IOException("Release reply lost")
        };
        var logger = new RecordingLogger();
        var writer = CreateWriter(new FakeRuntime(true), client, new RecordingEndpoint(), logger: logger);

        var result = await writer.PulseAsync(Address(area), 1);

        AssertFailure("ModbusBitPulseReleaseFailed", result);
        Assert.Contains("Release reply lost", result.ErrorDetails);
        Assert.Equal([true, false], client.Attempts);
        Assert.True(Assert.Single(client.Writes).Value);
        Assert.Contains(logger.Entries, entry =>
            entry.Level >= LogLevel.Warning
            && entry.Exception?.Message.Contains("Release reply lost", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData(ModbusDataArea.Coil)]
    [InlineData(ModbusDataArea.HoldingRegister)]
    public async Task PulseAsync_ResetsAfterAmbiguousActivationWithoutRetryingIt(ModbusDataArea area)
    {
        var client = new RecordingEndpoint
        {
            AfterWrite = value =>
            {
                if (value)
                    throw new IOException("Activation reply lost");
            }
        };
        var writer = CreateWriter(new FakeRuntime(true), client, new RecordingEndpoint());

        var result = await writer.PulseAsync(Address(area), 1);

        AssertFailure("ModbusBitPulseRisingEdgeFailed", result);
        Assert.Contains("Activation reply lost", result.ErrorDetails);
        Assert.Equal([true, false], client.Attempts);
        Assert.Equal([true, false], client.Writes.Select(write => write.Value));
    }

    [Fact]
    public async Task PulseAsync_ReportsBothErrorsWhenActivationAndReleaseAreUnconfirmed()
    {
        var client = new RecordingEndpoint
        {
            AfterWrite = value => throw new IOException(value ? "Activation reply lost" : "Release reply lost")
        };
        var writer = CreateWriter(new FakeRuntime(true), client, new RecordingEndpoint());

        var result = await writer.PulseAsync(Address(), 1);

        AssertFailure("ModbusBitPulseReleaseFailed", result);
        Assert.Contains("Activation reply lost", result.ErrorDetails);
        Assert.Contains("Release reply lost", result.ErrorDetails);
        Assert.Equal([true, false], client.Attempts);
    }

    [Fact]
    public async Task PulseAsync_CancellationDuringHoldStillReleasesWithIndependentToken()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new RecordingEndpoint();
        var writer = CreateWriter(new FakeRuntime(true), client, new RecordingEndpoint());

        var pulse = writer.PulseAsync(Address(), 60_000, cancellation.Token);
        Assert.True(Assert.Single(client.Writes).Value);
        await cancellation.CancelAsync();
        var result = await pulse.WaitAsync(TimeSpan.FromSeconds(5));

        AssertFailure("ModbusBitPulseCancelled", result);
        Assert.Equal([true, false], client.Writes.Select(write => write.Value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PulseAsync_ReleaseTimeoutIsBoundedAndReportsUncertainState(bool cancelHold)
    {
        using var cancellation = new CancellationTokenSource();
        var client = new RecordingEndpoint
        {
            BeforeWrite = (value, token) => value ? Task.CompletedTask : Task.Delay(Timeout.InfiniteTimeSpan, token)
        };
        var writer = CreateWriter(new FakeRuntime(true), client, new RecordingEndpoint(), edgeTimeout: TimeSpan.FromMilliseconds(100));

        var pulse = writer.PulseAsync(Address(), cancelHold ? 60_000 : 1, cancellation.Token);
        if (cancelHold)
        {
            Assert.True(Assert.Single(client.Writes).Value);
            await cancellation.CancelAsync();
        }
        var result = await pulse.WaitAsync(TimeSpan.FromSeconds(5));

        AssertFailure("ModbusBitPulseReleaseFailed", result);
        Assert.Equal([true, false], client.Attempts);
        Assert.True(Assert.Single(client.Writes).Value);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task PulseAsync_RoleSwitchKeepsReleaseOnOriginalTarget(bool startWithClient, bool originalAvailable)
    {
        var runtime = new FakeRuntime(startWithClient);
        var original = new RecordingEndpoint();
        var replacement = new RecordingEndpoint();
        var address = Address();
        original.AfterWrite = value =>
        {
            if (value)
            {
                runtime.UseClient = !startWithClient;
                address.Address = 42;
            }
        };
        if (!originalAvailable)
        {
            original.BeforeWrite = (value, _) => value
                ? Task.CompletedTask
                : throw new ModbusBitPulseNotSentException("Original target stopped");
        }
        var writer = CreateWriter(runtime, startWithClient ? original : replacement, startWithClient ? replacement : original);

        var result = await writer.PulseAsync(address, 1);

        if (originalAvailable)
        {
            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Equal([true, false], original.Writes.Select(write => write.Value));
        }
        else
        {
            AssertFailure("ModbusBitPulseReleaseFailed", result);
            Assert.True(Assert.Single(original.Writes).Value);
        }
        Assert.All(original.Writes, write => Assert.Equal(2, write.Address));
        Assert.Equal([true, false], original.Attempts);
        Assert.Equal(1, original.Captures);
        Assert.Equal(0, replacement.Captures);
        Assert.Empty(replacement.Writes);
    }

    [Theory]
    [InlineData(ModbusDataArea.Coil, true)]
    [InlineData(ModbusDataArea.HoldingRegister, true)]
    [InlineData(ModbusDataArea.HoldingRegister, false)]
    public async Task PulseAsync_KnownUnsentEdgeNeverUsingSnapshotForFallback(ModbusDataArea area, bool unreadableOnRise)
    {
        var client = new RecordingEndpoint
        {
            BeforeWrite = (value, _) => value == unreadableOnRise
                ? throw new ModbusBitPulseNotSentException("Fresh read unavailable")
                : Task.CompletedTask
        };
        var server = new RecordingEndpoint();
        var runtime = new FakeRuntime(true)
        {
            ClientSnapshot = new ModbusSnapshot { Role = ModbusRuntimeRole.Client, HoldingRegisters = [1, 2, 3] },
            ServerSnapshot = new ModbusSnapshot { Role = ModbusRuntimeRole.Server, HoldingRegisters = [4, 5, 6] }
        };
        var writer = CreateWriter(runtime, client, server);

        var result = await writer.PulseAsync(Address(area), 1);

        AssertFailure(unreadableOnRise ? "ModbusBitPulseRisingEdgeFailed" : "ModbusBitPulseReleaseFailed", result);
        Assert.Equal(unreadableOnRise ? [true] : [true, false], client.Attempts);
        Assert.Equal(unreadableOnRise ? [] : [true], client.Writes.Select(write => write.Value));
        Assert.Empty(server.Attempts);
    }

    [Theory]
    [InlineData((ModbusDataArea)99, 2, null, 1, "ModbusBitAreaUnsupported")]
    [InlineData(ModbusDataArea.Coil, 2, 0, 1, "ModbusBitCoilBitIndexInvalid")]
    [InlineData(ModbusDataArea.HoldingRegister, 2, null, 1, "ModbusBitRegisterBitInvalid")]
    [InlineData(ModbusDataArea.HoldingRegister, 2, -1, 1, "ModbusBitRegisterBitInvalid")]
    [InlineData(ModbusDataArea.HoldingRegister, 2, 16, 1, "ModbusBitRegisterBitInvalid")]
    [InlineData(ModbusDataArea.Coil, 2, null, 0, "ModbusBitPulseDurationInvalid")]
    [InlineData(ModbusDataArea.Coil, 2, null, 60_001, "ModbusBitPulseDurationInvalid")]
    [InlineData(ModbusDataArea.Coil, -1, null, 1, "ModbusBitAddressInvalid")]
    [InlineData(ModbusDataArea.Coil, 65_536, null, 1, "ModbusBitAddressInvalid")]
    public async Task PulseAsync_RejectsInvalidInputBeforeCapturingTarget(
        ModbusDataArea area, int address, int? bit, int duration, string expectedError)
    {
        var client = new RecordingEndpoint();
        var writer = CreateWriter(new FakeRuntime(true), client, new RecordingEndpoint());

        var result = await writer.PulseAsync(new ModbusBitAddressOptions { Area = area, Address = address, BitIndex = bit }, duration);

        AssertFailure(expectedError, result);
        Assert.Equal(0, client.Captures);
        Assert.Empty(client.Attempts);
    }

    [Fact]
    public async Task PulseAsync_ConcurrentPulsesDoNotOverlap()
    {
        var unblockFirstEdge = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RecordingEndpoint { BeforeWrite = (value, token) => value ? unblockFirstEdge.Task.WaitAsync(token) : Task.CompletedTask };
        var writer = CreateWriter(new FakeRuntime(true), client, new RecordingEndpoint());
        var secondAddress = Address();
        secondAddress.Address = 3;

        var first = writer.PulseAsync(Address(), 1);
        var second = writer.PulseAsync(secondAddress, 1);
        Assert.False(second.IsCompleted);
        Assert.Equal(1, client.Captures);
        Assert.True(Assert.Single(client.Attempts));
        unblockFirstEdge.SetResult();
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.All(results, result => Assert.True(result.Succeeded, result.ErrorMessage));
        Assert.Equal([(2, true), (2, false), (3, true), (3, false)], client.Writes.Select(write => (write.Address, write.Value)));
    }

    [Fact]
    public async Task PulseAsync_CancellationWhileQueuedDoesNotCaptureOrWrite()
    {
        using var cancellation = new CancellationTokenSource();
        var releaseFirstEdge = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RecordingEndpoint
        {
            BeforeWrite = (value, token) => value ? releaseFirstEdge.Task.WaitAsync(token) : Task.CompletedTask
        };
        var writer = CreateWriter(new FakeRuntime(true), client, new RecordingEndpoint());
        var first = writer.PulseAsync(Address(), 1);

        var queued = writer.PulseAsync(Address(), 1, cancellation.Token);
        await cancellation.CancelAsync();
        var result = await queued.WaitAsync(TimeSpan.FromSeconds(5));

        releaseFirstEdge.SetResult();
        var firstResult = await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(firstResult.Succeeded, firstResult.ErrorMessage);
        AssertFailure("ModbusBitPulseCancelled", result);
        Assert.Equal(1, client.Captures);
        Assert.Equal([true, false], client.Attempts);
    }

    [Fact]
    public async Task PulseAsync_WithoutRunningRoleDoesNotCaptureTarget()
    {
        var client = new RecordingEndpoint();
        var server = new RecordingEndpoint();
        var writer = CreateWriter(new FakeRuntime(null), client, server);

        var result = await writer.PulseAsync(Address(), 1);

        AssertFailure("ModbusNotRunning", result);
        Assert.Equal(0, client.Captures);
        Assert.Equal(0, server.Captures);
    }

    private static ModbusBitWriter CreateWriter(
        IModbusRuntimeService runtime,
        IModbusClientService client,
        IModbusServerService server,
        TimeSpan? edgeTimeout = null,
        ILogger<ModbusBitWriter>? logger = null)
        => new(runtime, client, server, logger ?? NullLogger<ModbusBitWriter>.Instance, edgeTimeout: edgeTimeout);

    private static ModbusBitAddressOptions Address(ModbusDataArea area = ModbusDataArea.Coil)
        => new() { Area = area, Address = 2, BitIndex = area == ModbusDataArea.HoldingRegister ? 3 : null };

    private static void AssertFailure(string errorCode, ModbusOperationResult result)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(errorCode, result.ErrorCode);
    }

    private sealed class StaticOptionsMonitor(ModbusOptions options) : IOptionsMonitor<ModbusOptions>
    {
        public ModbusOptions CurrentValue => options;
        public ModbusOptions Get(string? name) => options;
        public IDisposable? OnChange(Action<ModbusOptions, string?> listener) => null;
    }

    internal sealed class FakeRuntime(bool? useClient) : IModbusRuntimeService
    {
        public bool? UseClient { get; set; } = useClient;
        public ModbusStatus Status => new()
        {
            ClientState = UseClient == true ? ModbusConnectionState.Running : ModbusConnectionState.Stopped,
            ServerState = UseClient == false ? ModbusConnectionState.Running : ModbusConnectionState.Stopped
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

    private sealed class RecordingEndpoint : IModbusClientService, IModbusServerService, IModbusBitPulseProvider
    {
        public int Captures { get; private set; }
        public List<bool> Attempts { get; } = [];
        public List<Edge> Writes { get; } = [];
        public Func<bool, CancellationToken, Task> BeforeWrite { get; set; } = (_, _) => Task.CompletedTask;
        public Action<bool> AfterWrite { get; set; } = _ => { };
        public ModbusConnectionState State => ModbusConnectionState.Running;
        public ModbusSnapshot Snapshot => ModbusSnapshot.Empty;
        public event EventHandler<ModbusStatus>? StatusChanged { add { } remove { } }
        public event EventHandler<ModbusSnapshot>? SnapshotChanged { add { } remove { } }
        public Task StartAsync(ModbusEndpointOptions options, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteCoilAsync(int address, bool value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task WriteRegisterAsync(int address, ushort value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task WriteRegistersAsync(int startAddress, ushort[] values, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetCoilAsync(int address, bool value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetRegisterAsync(int address, ushort value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetRegistersAsync(int startAddress, ushort[] values, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public IModbusBitPulseTarget CaptureBitPulseTarget(ModbusBitAddressOptions address)
        {
            Captures++;
            return new Target(this, address);
        }

        private sealed class Target(RecordingEndpoint endpoint, ModbusBitAddressOptions address) : IModbusBitPulseTarget
        {
            public string Description => $"Test endpoint {address.Area} {address.Address}";

            public async Task WriteAsync(bool value, CancellationToken cancellationToken)
            {
                endpoint.Attempts.Add(value);
                cancellationToken.ThrowIfCancellationRequested();
                await endpoint.BeforeWrite(value, cancellationToken);
                endpoint.Writes.Add(new Edge(address.Area, address.Address, address.BitIndex, value));
                endpoint.AfterWrite(value);
            }
        }
    }

    private sealed record Edge(ModbusDataArea Area, int Address, int? BitIndex, bool Value);

    private sealed class RecordingLogger : ILogger<ModbusBitWriter>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, exception));
    }
}
