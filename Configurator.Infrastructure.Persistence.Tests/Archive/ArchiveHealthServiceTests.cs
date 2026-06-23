using Configurator.Application.Services.Archiving;
using Configurator.Infrastructure.Persistence.Archive;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Archive;

public sealed class ArchiveHealthServiceTests
{
    [Fact]
    public void HealthService_ObserverReceivesCurrentAndPublishedHealth()
    {
        var service = new ArchiveHealthService();
        var observer = new RecordingObserver();

        using var subscription = service.Observe().Subscribe(observer);
        service.Publish(new ArchiveHealth(
            ArchiveHealthState.Healthy,
            DateTimeOffset.UtcNow,
            "Archive running.",
            queueDepth: 2,
            droppedTelemetryCount: 3,
            lastSuccessAtUtc: DateTimeOffset.UtcNow));

        Assert.Equal([ArchiveHealthState.Stopped, ArchiveHealthState.Healthy], observer.Values.Select(value => value.State).ToArray());
        Assert.Equal(2, service.Current.QueueDepth);
        Assert.Equal(3, service.Current.DroppedTelemetryCount);
    }

    private sealed class RecordingObserver : IObserver<ArchiveHealth>
    {
        public List<ArchiveHealth> Values { get; } = [];

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(ArchiveHealth value)
            => Values.Add(value);
    }
}
