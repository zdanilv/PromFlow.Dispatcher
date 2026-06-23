using Configurator.Application.Services.Archiving;
using Configurator.Infrastructure.Persistence.Archive;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Archive;

public sealed class ArchivePriorityBufferTests
{
    [Fact]
    public async Task PriorityBuffer_CriticalFull_WaitsAndCancellationReturnsArchiveEnqueueCanceled()
    {
        var buffer = CreateBuffer(channelCapacity: 4);
        buffer.Open();
        var first = await buffer.EnqueueAsync(CreateEnvelope(ArchivePriority.Critical), CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));

        var second = await buffer.EnqueueAsync(CreateEnvelope(ArchivePriority.Critical), timeout.Token);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(ArchivePersistenceErrorCodes.ArchiveEnqueueCanceled, second.ErrorCode);
        Assert.Equal(1, buffer.Snapshot.CriticalDepth);
    }

    [Fact]
    public async Task PriorityBuffer_NormalFull_WaitsAndCancellationReturnsArchiveEnqueueCanceled()
    {
        var buffer = CreateBuffer(channelCapacity: 4);
        buffer.Open();
        var first = await buffer.EnqueueAsync(CreateEnvelope(ArchivePriority.Normal), CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));

        var second = await buffer.EnqueueAsync(CreateEnvelope(ArchivePriority.Normal), timeout.Token);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(ArchivePersistenceErrorCodes.ArchiveEnqueueCanceled, second.ErrorCode);
        Assert.Equal(1, buffer.Snapshot.NormalDepth);
    }

    [Fact]
    public async Task PriorityBuffer_TelemetryOverload_CoalescesLatestAndIncrementsDroppedCount()
    {
        var buffer = CreateBuffer(channelCapacity: 4);
        buffer.Open();
        var envelopes = Enumerable.Range(0, 5)
            .Select(_ => CreateEnvelope(ArchivePriority.Telemetry))
            .ToArray();

        foreach (var envelope in envelopes)
        {
            var result = await buffer.EnqueueAsync(envelope, CancellationToken.None);
            Assert.True(result.Succeeded);
        }

        var snapshot = buffer.Snapshot;
        var drained = buffer.Drain(10);

        Assert.True(snapshot.HasTelemetry);
        Assert.Equal(1, snapshot.QueueDepth);
        Assert.Equal(4, snapshot.DroppedTelemetryCount);
        Assert.Single(drained);
        Assert.Equal(envelopes[^1].Id, drained[0].Id);
    }

    [Fact]
    public async Task PriorityBuffer_Drain_ReturnsCriticalThenNormalThenTelemetry()
    {
        var buffer = CreateBuffer(channelCapacity: 8);
        buffer.Open();
        var normal = CreateEnvelope(ArchivePriority.Normal);
        var telemetry = CreateEnvelope(ArchivePriority.Telemetry);
        var critical = CreateEnvelope(ArchivePriority.Critical);

        Assert.True((await buffer.EnqueueAsync(normal, CancellationToken.None)).Succeeded);
        Assert.True((await buffer.EnqueueAsync(telemetry, CancellationToken.None)).Succeeded);
        Assert.True((await buffer.EnqueueAsync(critical, CancellationToken.None)).Succeeded);

        var drained = buffer.Drain(10);

        Assert.Equal([critical.Id, normal.Id, telemetry.Id], drained.Select(envelope => envelope.Id).ToArray());
    }

    private static ArchivePriorityBuffer CreateBuffer(int channelCapacity)
        => new(Options.Create(new ArchiveOptions
        {
            Enabled = true,
            DeviceId = "device-1",
            ChannelCapacity = channelCapacity,
            BatchSize = 1,
            BatchFlushIntervalMs = 100,
            BusyTimeoutMs = 100
        }));

    private static ArchiveEnvelope CreateEnvelope(ArchivePriority priority)
        => new(Guid.NewGuid(), ArchiveRecordKind.RawModbusSnapshot, priority, new object(), DateTimeOffset.UtcNow);
}
