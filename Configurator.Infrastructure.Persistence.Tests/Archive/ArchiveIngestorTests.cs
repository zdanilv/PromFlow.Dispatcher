using Configurator.Application.Services.Archiving;
using Configurator.Infrastructure.Persistence.Archive;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Archive;

public sealed class ArchiveIngestorTests
{
    [Fact]
    public async Task Ingestor_DisabledArchive_ReturnsSuccessWithoutQueueing()
    {
        var options = Options.Create(new ArchiveOptions { Enabled = false, ChannelCapacity = 4, BatchSize = 1 });
        var buffer = new ArchivePriorityBuffer(options);
        var ingestor = new ArchiveIngestor(buffer, options);

        var result = await ingestor.EnqueueAsync(CreateEnvelope(ArchivePriority.Normal), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, buffer.Snapshot.QueueDepth);
    }

    [Fact]
    public async Task Ingestor_EnabledArchive_DelegatesToPriorityBuffer()
    {
        var options = Options.Create(new ArchiveOptions
        {
            Enabled = true,
            DeviceId = "device-1",
            ChannelCapacity = 4,
            BatchSize = 1,
            BatchFlushIntervalMs = 100,
            BusyTimeoutMs = 100
        });
        var buffer = new ArchivePriorityBuffer(options);
        buffer.Open();
        var ingestor = new ArchiveIngestor(buffer, options);

        var result = await ingestor.EnqueueAsync(CreateEnvelope(ArchivePriority.Normal), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, buffer.Snapshot.NormalDepth);
    }

    private static ArchiveEnvelope CreateEnvelope(ArchivePriority priority)
        => new(Guid.NewGuid(), ArchiveRecordKind.RawModbusSnapshot, priority, new object(), DateTimeOffset.UtcNow);
}
