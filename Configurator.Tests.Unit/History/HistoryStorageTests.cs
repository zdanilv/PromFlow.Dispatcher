using Configurator.Application.Services.History;
using Configurator.Infrastructure.Services.History;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Configurator.Tests.Unit.History;

public sealed class HistoryStorageTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PromFlowHistoryTests", Guid.NewGuid().ToString("N"));
    private readonly List<SqliteHistoryEventStore> _stores = [];

    public Task InitializeAsync() { Directory.CreateDirectory(_directory); return Task.CompletedTask; }

    private SqliteHistoryEventStore Store()
    {
        var store = new SqliteHistoryEventStore(Path.Combine(_directory, "events.db"));
        _stores.Add(store);
        return store;
    }

    private static HistoryEvent Event(int index) => new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(index),
        DateTimeOffset.UtcNow, "Получено", "equip.ковш.power", "PowerFeedback", "Ковш", null, null, null,
        index.ToString(System.Globalization.CultureInfo.InvariantCulture), "Mock", Guid.NewGuid());

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    [InlineData(100000)]
    public async Task AppendAndReopen_PreservesCompleteEventsAndContiguousIds(int count)
    {
        var store = Store();
        await store.InitializeAsync();
        for (var offset = 0; offset < count; offset += 200)
        {
            var batch = Enumerable.Range(offset, Math.Min(200, count - offset)).Select(Event).ToArray();
            Assert.Equal(batch.Length, await store.AppendBatchAsync(batch));
        }
        Assert.Equal(count, await store.GetMaximumIdAsync());
        await store.DisposeAsync();
        var reopened = Store();
        await reopened.InitializeAsync();
        long previous = 0;
        await foreach (var item in reopened.ReadRangeAsync(0, count))
        {
            Assert.Equal(++previous, item.Id);
            Assert.Equal("equip.ковш.power", item.Name);
            Assert.Null(item.Address);
            Assert.Equal(TimeSpan.Zero, item.TimestampUtc.Offset);
        }
        Assert.Equal(count, previous);
    }

    [Fact]
    public async Task DuplicateEventUid_DoesNotDuplicateOrConsumeId()
    {
        var store = Store();
        await store.InitializeAsync();
        var first = Event(0);
        Assert.Equal(1, await store.AppendBatchAsync([first]));
        Assert.Equal(0, await store.AppendBatchAsync([first]));
        Assert.Equal(1, await store.AppendBatchAsync([Event(1)]));
        Assert.Equal(2, await store.GetMaximumIdAsync());
    }

    [Fact]
    public async Task ExclusiveLease_RejectsSecondOwnerThenAllowsAfterRelease()
    {
        var first = Store();
        await first.InitializeAsync();
        var second = Store();
        await Assert.ThrowsAsync<HistoryStorageInUseException>(() => second.InitializeAsync());
        await first.DisposeAsync();
        await second.InitializeAsync();
        Assert.True(second.IsInitialized);
    }

    [Fact]
    public async Task ArchiveCommit_IsAtomicWithCursorAndIntentRemoval()
    {
        var store = Store();
        await store.InitializeAsync();
        await store.AppendBatchAsync([Event(0), Event(1)]);
        var now = DateTimeOffset.UtcNow;
        var path = Path.Combine(_directory, "own.zip");
        await store.SaveArchiveIntentAsync(new("intent", path + ".tmp", path, 1, 2, 2, "hash", now));
        var run = new SqliteArchiveRun(1, 2, 2, path, "hash", now, now);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.RegisterArchiveAsync(run, 1));
        Assert.Equal(0, await store.GetLastArchivedIdAsync());
        Assert.Single(await store.GetArchiveIntentsAsync());
        await store.RegisterArchiveAsync(run, 0);
        Assert.Equal(2, await store.GetLastArchivedIdAsync());
        Assert.Empty(await store.GetArchiveIntentsAsync());
        Assert.Single(await store.GetArchiveRunsAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.RegisterArchiveAsync(run, 0));
    }

    [Fact]
    public async Task Retention_RequiresConfirmedRangeAndLeavesNewEvents()
    {
        var store = Store();
        await store.InitializeAsync();
        var old = Event(0) with { TimestampUtc = DateTimeOffset.UtcNow.AddDays(-60) };
        await store.AppendBatchAsync([old, Event(1)]);
        Assert.Equal(0, await store.DeleteArchivedEventsAsync(1, 1, DateTimeOffset.UtcNow.AddDays(-30)));
        var now = DateTimeOffset.UtcNow;
        await store.RegisterArchiveAsync(new(1, 1, 1, "archive.zip", "hash", now, now), 0);
        Assert.True(await store.HasArchivedEventsBeforeAsync(1, 1, now.AddDays(-30)));
        Assert.Equal(1, await store.DeleteArchivedEventsAsync(1, 1, now.AddDays(-30)));
        Assert.False(await store.HasArchivedEventsBeforeAsync(1, 1, now.AddDays(-30)));
        var remaining = new List<HistoryEvent>();
        await foreach (var item in store.ReadRangeAsync(0, 2)) remaining.Add(item);
        Assert.Equal(2, Assert.Single(remaining).Id);
        Assert.Equal(1, await store.GetLastArchivedIdAsync());
    }

    [Fact]
    public async Task Writer_FlushAndStopCommitAllAcceptedEvents()
    {
        var store = Store();
        await using var writer = new HistoryEventWriter(store);
        await writer.StartAsync();
        for (var i = 0; i < 1000; i++) Assert.True(writer.TryWrite(Event(i)));
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await writer.FlushAsync(budget.Token);
        Assert.Equal(1000, writer.PersistedEventsCount);
        Assert.Equal(0, writer.PendingQueueCount);
        await writer.StopAsync(budget.Token);
        Assert.Equal(1000, await store.GetMaximumIdAsync());
        Assert.False(writer.TryWrite(Event(1001)));
        Assert.Equal(1, writer.RejectedEventsCount);
    }

    [Fact]
    public async Task Writer_StorageFailureAndOverflowAreVisibleAndBounded()
    {
        var store = Store();
        await store.InitializeAsync();
        var blocked = Store();
        await using var writer = new HistoryEventWriter(blocked, capacity: 2);
        await writer.StartAsync();
        Assert.True(writer.TryWrite(Event(0)));
        Assert.True(writer.TryWrite(Event(1)));
        Assert.False(writer.TryWrite(Event(2)));
        Assert.Equal(2, writer.PendingQueueCount);
        Assert.Equal(1, writer.RejectedEventsCount);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.FlushAsync(timeout.Token));
        Assert.NotNull(writer.LastErrorCode);
        await store.DisposeAsync();
        using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await writer.FlushAsync(recovery.Token);
        Assert.Equal(2, writer.PersistedEventsCount);
        await writer.StopAsync(recovery.Token);
    }

    [Fact]
    public async Task Writer_ConcurrentProducersCommitEachAcceptedEventOnce()
    {
        var store = Store();
        await using var writer = new HistoryEventWriter(store);
        await writer.StartAsync();
        var events = Enumerable.Range(0, 4000).Select(Event).ToArray();
        await Task.WhenAll(events.Chunk(500).Select(batch => Task.Run(() =>
        {
            foreach (var item in batch) Assert.True(writer.TryWrite(item));
        })));
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await writer.StopAsync(budget.Token);
        var ids = new HashSet<Guid>();
        await foreach (var item in store.ReadRangeAsync(0, 4000)) Assert.True(ids.Add(item.EventUid));
        Assert.Equal(4000, ids.Count);
        Assert.Equal(4000, writer.PersistedEventsCount);
        Assert.Equal(0, writer.PendingQueueCount);
    }

    [Fact]
    public async Task Writer_RejectionLogsAwayFromProducerAndStopIsFinal()
    {
        using var logger = new CapturingLogger();
        var store = Store();
        var writer = new HistoryEventWriter(store, logger);
        var producerThread = Environment.CurrentManagedThreadId;
        Assert.False(writer.TryWrite(Event(0)));
        Assert.True(logger.Logged.Wait(TimeSpan.FromSeconds(5)));
        Assert.NotEqual(producerThread, logger.LoggingThread);
        await writer.StopAsync();
        await writer.StopAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.StartAsync());
        await writer.DisposeAsync();
        await writer.DisposeAsync();
    }

    [Fact]
    public async Task ExpiredRun_PreservesCursorAcrossRestart()
    {
        var store = Store();
        await store.InitializeAsync();
        await store.AppendBatchAsync([Event(0)]);
        var now = DateTimeOffset.UtcNow;
        await store.RegisterArchiveAsync(new(1, 1, 1, "expired.zip", "hash", now, now), 0);
        await store.MarkArchiveExpiredAsync("expired.zip");
        await store.SetMetadataAsync("lastScheduledCheckUtc", now.ToString("O"));
        await store.DisposeAsync();
        var reopened = Store();
        await reopened.InitializeAsync();
        Assert.Equal(1, await reopened.GetLastArchivedIdAsync());
        Assert.True(Assert.Single(await reopened.GetArchiveRunsAsync()).Expired);
        Assert.Equal(now.ToString("O"), await reopened.GetMetadataAsync("lastScheduledCheckUtc"));
    }

    [Fact]
    public async Task CorruptDatabase_IsPreservedAndNotReplaced()
    {
        var path = Path.Combine(_directory, "events.db");
        byte[] corrupt = [1, 3, 5, 7, 9];
        await File.WriteAllBytesAsync(path, corrupt);
        var store = Store();
        await Assert.ThrowsAnyAsync<Exception>(() => store.InitializeAsync());
        Assert.Equal(corrupt, await File.ReadAllBytesAsync(path));
    }

    public async Task DisposeAsync()
    {
        foreach (var store in _stores) await store.DisposeAsync();
        Directory.Delete(_directory, recursive: true);
    }

    private sealed class CapturingLogger : ILogger<HistoryEventWriter>, IDisposable
    {
        public ManualResetEventSlim Logged { get; } = new();
        public int LoggingThread { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            LoggingThread = Environment.CurrentManagedThreadId;
            Logged.Set();
        }
        public void Dispose() => Logged.Dispose();
    }
}
