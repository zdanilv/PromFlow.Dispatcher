using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Signals;
using Configurator.Infrastructure.Modbus.RouteMap;
using Configurator.Infrastructure.Modbus.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed partial class ModbusTcpServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Quality_RoleChangeRejectsAcquisitionFromPreviousRole(bool startWithClient)
    {
        var runtime = new FakeRuntimeService();
        var options = CreateOptions();
        await using var service = CreateService(runtime, new NoopClientService(), new NoopServerService(), options);
        if (startWithClient) await service.StartClientAsync();
        else await service.StartServerAsync();
        using var provider = new ModbusTcpSignalValueProvider(service, service, new TestOptionsMonitor(options),
            Options.Create(new RouteMapRuntimeOptions()), NullLogger<ModbusTcpSignalValueProvider>.Instance);
        var observer = new QualityObserver();
        using var subscription = provider.Observe().Subscribe(observer);
        var oldRole = startWithClient ? ModbusRuntimeRole.Client : ModbusRuntimeRole.Server;
        var old = QualitySnapshot(oldRole, [0, 5, 2]);
        runtime.PublishRawSnapshot(old);
        Assert.True(observer.Latest["DemoInput"].IsQualityGood);

        if (startWithClient) await service.StartServerAsync();
        else await service.StartClientAsync();
        Assert.Empty(service.CurrentSnapshot.Values);
        Assert.False(observer.Latest["DemoInput"].IsQualityGood);
        runtime.PublishRawSnapshot(old);
        Assert.Empty(service.CurrentSnapshot.Values);
        Assert.False(observer.Latest["DemoInput"].IsQualityGood);

        var next = QualitySnapshot(startWithClient ? ModbusRuntimeRole.Server : ModbusRuntimeRole.Client, [0, 5, 2]);
        runtime.PublishRawSnapshot(next);
        Assert.True(observer.Latest["DemoInput"].IsQualityGood);
        Assert.Equal(next.SourceSessionId, service.CurrentSnapshot.SourceSessionId);
    }

    [Fact]
    public async Task Quality_MapChangeRejectsQueuedOldAcquisitionAndRequiresNewRead()
    {
        var runtime = new FakeRuntimeService();
        var options = CreateOptions();
        await using var service = CreateService(runtime, new NoopClientService(), new NoopServerService(), options);
        await service.StartClientAsync();
        using var provider = new ModbusTcpSignalValueProvider(service, service, new TestOptionsMonitor(options),
            Options.Create(new RouteMapRuntimeOptions()), NullLogger<ModbusTcpSignalValueProvider>.Instance);
        var observer = new QualityObserver();
        using var subscription = provider.Observe().Subscribe(observer);
        var old = QualitySnapshot(ModbusRuntimeRole.Client, [0, 5, 9]);
        runtime.PublishRawSnapshot(old);
        Assert.True(observer.Latest["DemoInput"].IsQualityGood);
        var queued = QualitySnapshot(ModbusRuntimeRole.Client, [0, 5, 9], old.SourceSessionId);
        var map = options.DataMap.Select(point => point.Clone()).ToArray();
        map.Single(point => point.Name == "DemoInput").Address = 2;

        Assert.True(service.ApplyDataMap(map).Succeeded);
        runtime.PublishRawSnapshot(queued);
        Assert.Empty(service.CurrentSnapshot.Values);
        Assert.False(observer.Latest["DemoInput"].IsQualityGood);
        runtime.PublishRawSnapshot(QualitySnapshot(ModbusRuntimeRole.Client, [0, 5, 9], old.SourceSessionId));
        Assert.Equal((ushort)9, service.CurrentSnapshot.Values["DemoInput"].Value);
        Assert.True(observer.Latest["DemoInput"].IsQualityGood);
    }

    [Fact]
    public async Task Quality_RepeatedStartWithMapChangeClearsProviderBeforeNextRead()
    {
        var runtime = new FakeRuntimeService();
        var options = CreateOptions();
        await using var service = CreateService(runtime, new NoopClientService(), new NoopServerService(), options);
        await service.StartClientAsync();
        using var provider = new ModbusTcpSignalValueProvider(service, service, new TestOptionsMonitor(options),
            Options.Create(new RouteMapRuntimeOptions()), NullLogger<ModbusTcpSignalValueProvider>.Instance);
        var observer = new QualityObserver();
        using var subscription = provider.Observe().Subscribe(observer);
        runtime.PublishRawSnapshot(QualitySnapshot(ModbusRuntimeRole.Client, [0, 5, 9]));
        Assert.True(observer.Latest["DemoInput"].IsQualityGood);
        var nextOptions = options.Clone();
        nextOptions.DataMap.Single(point => point.Name == "DemoInput").Address = 2;
        Assert.True((await service.StartClientAsync(nextOptions)).Succeeded);
        Assert.Empty(service.CurrentSnapshot.Values);
        Assert.False(observer.Latest["DemoInput"].IsQualityGood);
    }

    [Fact]
    public async Task Quality_BothKeepsServerVisibleWithoutUsingItForClientRmw()
    {
        var runtime = new FakeRuntimeService();
        var options = CreateOptions();
        options.WriteConfirmationTimeoutMs = 0;
        options.DataMap.Add(CreateRegisterBit("Bit0", 2, 0));
        var client = new NoopClientService();
        await using var service = CreateService(runtime, client, new NoopServerService(), options);
        runtime.PublishRunningRoles();
        runtime.PublishRawSnapshot(QualitySnapshot(ModbusRuntimeRole.Client, [0, 5, 2]));
        runtime.PublishRawSnapshot(QualitySnapshot(ModbusRuntimeRole.Server, [0, 9, 0]));
        Assert.Equal(ModbusRuntimeRole.Server, service.CurrentSnapshot.SourceRole);
        Assert.Equal((ushort)9, service.CurrentSnapshot.Values["DemoInput"].Value);
        Assert.True((await service.SetAsync("Bit0", true)).Succeeded);
        Assert.Equal((2, (ushort)3), Assert.Single(client.RegisterWrites));
    }

    private static ModbusSnapshot QualitySnapshot(ModbusRuntimeRole role, ushort[] registers, Guid? session = null)
    {
        var sequence = ModbusObservationSequence.Next();
        var now = DateTimeOffset.Now;
        return new ModbusSnapshot
        {
            Role = role, SourceSessionId = session ?? Guid.NewGuid(), ReadSequence = sequence,
            Coils = [false], HoldingRegisters = registers, Timestamp = now,
            CoilsReadState = new(true, true, true, now, ModbusObservationSequence.Next()),
            HoldingRegistersReadState = new(true, true, true, now, ModbusObservationSequence.Next())
        };
    }

    [Fact]
    public async Task Quality_OlderPausedSnapshotCannotRestoreGoodAfterNewerFailedRead()
    {
        var runtime = new FakeRuntimeService();
        await using var service = CreateService(runtime, new NoopClientService(), new NoopServerService(), CreateOptions());
        await service.StartClientAsync();
        var initial = QualitySnapshot(ModbusRuntimeRole.Client, [0, 5, 2]);
        runtime.PublishRawSnapshot(initial);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = service.Subscribe("DemoInput", value =>
        {
            if (Equals(value.Value, (ushort)9))
            {
                entered.TrySetResult();
                resume.Task.GetAwaiter().GetResult();
            }
        });
        var older = Task.Run(() => runtime.PublishRawSnapshot(QualitySnapshot(ModbusRuntimeRole.Client, [0, 9, 2], initial.SourceSessionId)));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var next = QualitySnapshot(ModbusRuntimeRole.Client, [0, 9, 2], initial.SourceSessionId);
            runtime.PublishRawSnapshot(CopyRead(next, next.CoilsReadState!,
                next.HoldingRegistersReadState! with { LastAttemptSucceeded = false }));
            Assert.False(service.CurrentSnapshot.Values.ContainsKey("DemoInput"));
        }
        finally
        {
            resume.TrySetResult();
            await older.WaitAsync(TimeSpan.FromSeconds(2));
        }
        Assert.False(service.CurrentSnapshot.Values.ContainsKey("DemoInput"));
        Assert.False((await service.GetAsync<ushort>("DemoInput")).Succeeded);
    }

    [Fact]
    public async Task Quality_DelayedNotificationCannotRestoreOldMapAfterReset()
    {
        var runtime = new FakeRuntimeService();
        var options = CreateOptions();
        await using var service = CreateService(runtime, new NoopClientService(), new NoopServerService(), options);
        await service.StartClientAsync();
        var initial = QualitySnapshot(ModbusRuntimeRole.Client, [0, 5, 2]);
        runtime.PublishRawSnapshot(initial);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.SnapshotChanged += (_, value) =>
        {
            if (value.Values.TryGetValue("DemoInput", out var point) && Equals(point.Value, (ushort)9))
            {
                entered.TrySetResult();
                resume.Task.GetAwaiter().GetResult();
            }
        };
        using var provider = new ModbusTcpSignalValueProvider(service, service, new TestOptionsMonitor(options),
            Options.Create(new RouteMapRuntimeOptions()), NullLogger<ModbusTcpSignalValueProvider>.Instance);
        var observer = new QualityObserver();
        using var subscription = provider.Observe().Subscribe(observer);
        Assert.True(observer.Latest["DemoInput"].IsQualityGood);
        var older = Task.Run(() => runtime.PublishRawSnapshot(QualitySnapshot(ModbusRuntimeRole.Client, [0, 9, 2], initial.SourceSessionId)));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var map = options.DataMap.Select(point => point.Clone()).ToArray();
            map.Single(point => point.Name == "DemoInput").Address = 2;
            Assert.True(service.ApplyDataMap(map).Succeeded);
            Assert.False(observer.Latest["DemoInput"].IsQualityGood);
        }
        finally
        {
            resume.TrySetResult();
            await older.WaitAsync(TimeSpan.FromSeconds(2));
        }
        Assert.False(observer.Latest["DemoInput"].IsQualityGood);
        Assert.Empty(service.CurrentSnapshot.Values);
    }

    [Fact]
    public async Task Quality_StatusOnlyUpdateCannotHideDelayedFailedAreaNotification()
    {
        var runtime = new FakeRuntimeService();
        var options = CreateOptions();
        await using var service = CreateService(runtime, new NoopClientService(), new NoopServerService(), options);
        await service.StartClientAsync();
        var initial = QualitySnapshot(ModbusRuntimeRole.Client, [0, 5, 2]);
        runtime.PublishRawSnapshot(initial);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.HoldingRegistersReadState is { LastAttemptSucceeded: false })
            {
                entered.TrySetResult();
                resume.Task.GetAwaiter().GetResult();
            }
        };
        using var provider = new ModbusTcpSignalValueProvider(service, service, new TestOptionsMonitor(options),
            Options.Create(new RouteMapRuntimeOptions()), NullLogger<ModbusTcpSignalValueProvider>.Instance);
        var observer = new QualityObserver();
        using var subscription = provider.Observe().Subscribe(observer);
        Assert.True(observer.Latest["DemoInput"].IsQualityGood);
        var next = QualitySnapshot(ModbusRuntimeRole.Client, [0, 5, 2], initial.SourceSessionId);
        var older = Task.Run(() => runtime.PublishRawSnapshot(CopyRead(next, next.CoilsReadState!,
            initial.HoldingRegistersReadState! with { LastAttemptSucceeded = false })));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var invalid = options.Clone();
            invalid.DataMap.Single(point => point.Name == "DemoInput").Address = -1;
            Assert.False((await service.StartClientAsync(invalid)).Succeeded);
            Assert.Equal(ModbusConnectionState.Running, service.State.ClientState);
        }
        finally
        {
            resume.TrySetResult();
            await older.WaitAsync(TimeSpan.FromSeconds(2));
        }
        Assert.False(observer.Latest["DemoInput"].IsQualityGood);
        Assert.True(observer.Latest["DemoInput"].IsStale);
    }

    private sealed class QualityObserver : IObserver<IReadOnlyDictionary<string, SignalValue>>
    {
        public IReadOnlyDictionary<string, SignalValue> Latest { get; private set; } = new Dictionary<string, SignalValue>();
        public void OnNext(IReadOnlyDictionary<string, SignalValue> value) => Latest = value;
        public void OnCompleted() { }
        public void OnError(Exception error) => throw error;
    }
}
