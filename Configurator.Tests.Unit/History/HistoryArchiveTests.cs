using System.IO.Compression;
using System.Text.Json;
using Configurator.Application.Services.History;
using Configurator.Infrastructure.Services.History;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Configurator.Tests.Unit.History;

public sealed class HistoryArchiveTests
{
    [Theory]
    [InlineData("../events.zip")]
    [InlineData("C:\\events.zip")]
    [InlineData("CON.zip")]
    [InlineData("archive_{bad}.zip")]
    [InlineData("archive.zip ")]
    [InlineData("events.txt")]
    [InlineData(".zip")]
    [InlineData("archive.part.zip")]
    [InlineData("archive .zip")]
    [InlineData("LPT1.zip")]
    public void UnsafeNamesAreRejected(string template) => Assert.Throws<ArgumentException>(() =>
        HistoryArchivePaths.Validate(new() { FileNameTemplate = template }));

    [Theory]
    [InlineData(14)]
    [InlineData(10081)]
    public void InvalidIntervalsAreRejected(int interval) => Assert.Throws<ArgumentException>(() =>
        HistoryArchivePaths.Validate(new() { ArchiveIntervalMinutes = interval }));

    [Fact]
    public void NamesUseUtcSequenceAndCollisionSuffix()
    {
        using var scope = new Scope();
        var options = scope.Options with { FileNameTemplate = "История_{date}_{seq}.zip" };
        var timestamp = new DateTimeOffset(2026, 11, 23, 12, 34, 56, TimeSpan.FromHours(3));
        var first = HistoryArchivePaths.GetUniquePath(options, timestamp, 2);
        Assert.EndsWith("История_20261123_093456_000002.zip", first);
        File.WriteAllText(first, "existing");
        Assert.EndsWith("000002_001.zip", HistoryArchivePaths.GetUniquePath(options, timestamp, 2));
        Directory.CreateDirectory(Path.Combine(scope.Directory, "История_20261123_093456_000002_001.zip"));
        Assert.EndsWith("000002_002.zip", HistoryArchivePaths.GetUniquePath(options, timestamp, 2));
        Assert.Equal("existing", File.ReadAllText(first));
    }

    [Fact]
    public async Task DisabledAutomaticArchivesStillPersistAndManualRangesDoNotOverlap()
    {
        await using var scope = new Scope();
        await scope.Service.StartAsync();
        Assert.True(scope.Writer.TryWrite(CreateEvent(1)));
        Assert.True(scope.Writer.TryWrite(CreateEvent(2)));
        await scope.Writer.FlushAsync();
        Assert.Equal(HistoryArchiveState.Disabled, scope.Service.CurrentStatus.State);
        Assert.Equal(2, await scope.Store.GetMaximumIdAsync());
        var first = await scope.ArchiveAsync();
        Assert.Equal(ArchiveOutcome.Success, first.Outcome);
        var manifest = await HistoryZipArchive.VerifyAsync(first.Path!);
        Assert.Equal((1L, 2L, 2L), (manifest.FirstEventId, manifest.LastEventId, manifest.EventCount));
        Assert.Equal(ArchiveOutcome.NoNewEvents, (await scope.ArchiveAsync()).Outcome);
        Assert.True(scope.Writer.TryWrite(CreateEvent(3)));
        var second = await scope.ArchiveAsync();
        var next = await HistoryZipArchive.VerifyAsync(second.Path!);
        Assert.Equal((3L, 3L, 1L), (next.FirstEventId, next.LastEventId, next.EventCount));
        using var zip = ZipFile.OpenRead(first.Path!);
        using var reader = new StreamReader(zip.GetEntry("events.jsonl")!.Open());
        var stored = JsonSerializer.Deserialize<HistoryEvent>((await reader.ReadLineAsync())!, HistoryZipArchive.JsonOptions)!;
        Assert.Equal("Карточка ковша", stored.Objects);
        Assert.Null(stored.Address);
        Assert.Equal(TimeSpan.Zero, stored.TimestampUtc.Offset);
        Assert.Equal("true", stored.Value);
    }

    [Fact]
    public async Task FailedWriteDoesNotAdvanceCursorAndCanRetry()
    {
        var files = new FaultFiles { FailCreate = true };
        await using var scope = new Scope(files);
        await scope.Service.StartAsync();
        Assert.True(scope.Writer.TryWrite(CreateEvent(1)));
        Assert.Equal(ArchiveOutcome.Error, (await scope.ArchiveAsync()).Outcome);
        Assert.Equal(0, await scope.Store.GetLastArchivedIdAsync());
        Assert.Equal(1, await scope.Store.GetMaximumIdAsync());
        Assert.Equal(HistoryArchiveState.Error, scope.Service.CurrentStatus.State);
        files.FailCreate = false;
        Assert.Equal(ArchiveOutcome.Success, (await scope.ArchiveAsync()).Outcome);
    }

    [Fact]
    public async Task PublishedOrphanIsRecoveredWithoutDuplicateCoverage()
    {
        var files = new FaultFiles { FailAfterMove = true };
        await using var scope = new Scope(files);
        await scope.Service.StartAsync();
        Assert.True(scope.Writer.TryWrite(CreateEvent(1)));
        Assert.Equal(ArchiveOutcome.Error, (await scope.ArchiveAsync()).Outcome);
        Assert.Equal(0, await scope.Store.GetLastArchivedIdAsync());
        Assert.Single(Directory.GetFiles(scope.Directory, "*.zip"));
        files.FailAfterMove = false;
        Assert.Equal(ArchiveOutcome.NoNewEvents, (await scope.ArchiveAsync()).Outcome);
        Assert.Equal(1, await scope.Store.GetLastArchivedIdAsync());
        Assert.Single(await scope.Store.GetArchiveRunsAsync());
        Assert.Single(Directory.GetFiles(scope.Directory, "*.zip"));
    }

    [Fact]
    public async Task MissingPreparedArchiveDoesNotDiscardIntentOrCreateOverlappingRange()
    {
        var files = new FaultFiles { FailAfterMove = true };
        await using var scope = new Scope(files);
        await scope.Service.StartAsync();
        Assert.True(scope.Writer.TryWrite(CreateEvent(1)));
        Assert.Equal(ArchiveOutcome.Error, (await scope.ArchiveAsync()).Outcome);
        var zip = Assert.Single(Directory.GetFiles(scope.Directory, "*.zip"));
        File.Move(zip, zip + ".offline");
        files.FailAfterMove = false;

        Assert.Equal(ArchiveOutcome.Error, (await scope.ArchiveAsync()).Outcome);
        Assert.Equal(0, await scope.Store.GetLastArchivedIdAsync());
        Assert.Single(await scope.Store.GetArchiveIntentsAsync());
        Assert.Empty(Directory.GetFiles(scope.Directory, "*.zip"));

        File.Move(zip + ".offline", zip);
        Assert.Equal(ArchiveOutcome.NoNewEvents, (await scope.ArchiveAsync()).Outcome);
        Assert.Equal(1, await scope.Store.GetLastArchivedIdAsync());
    }

    [Fact]
    public async Task CorruptedPublishedArchiveBlocksCleanupAndCursorAdvance()
    {
        var files = new FaultFiles { FailAfterMove = true };
        await using var scope = new Scope(files);
        await scope.Service.StartAsync();
        Assert.True(scope.Writer.TryWrite(CreateEvent(1)));
        await scope.ArchiveAsync();
        var path = Assert.Single(Directory.GetFiles(scope.Directory, "*.zip"));
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            zip.GetEntry("events.jsonl")!.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("events.jsonl").Open());
            writer.WriteLine("{}");
        }
        files.FailAfterMove = false;
        Assert.Equal(ArchiveOutcome.Error, (await scope.ArchiveAsync()).Outcome);
        Assert.Equal(0, await scope.Store.GetLastArchivedIdAsync());
        Assert.Equal(1, await scope.Store.GetMaximumIdAsync());
    }

    [Fact]
    public async Task ConcurrentRequestsHaveOneArchiveOwner()
    {
        var files = new FaultFiles { PauseMove = true };
        await using var scope = new Scope(files);
        await scope.Service.StartAsync();
        Assert.True(scope.Writer.TryWrite(CreateEvent(1)));
        var first = Task.Run(() => scope.ArchiveAsync());
        Assert.True(files.MoveEntered.Wait(TimeSpan.FromSeconds(10)));
        Assert.Equal(ArchiveOutcome.AlreadyRunning, (await scope.Service.ArchiveNowAsync()).Outcome);
        files.AllowMove.Set();
        Assert.Equal(ArchiveOutcome.Success, (await first).Outcome);
        Assert.Single(await scope.Store.GetArchiveRunsAsync());
    }

    [Fact]
    public async Task SettingsValidationLeavesActiveOptionsAndRetentionPreservesForeignFiles()
    {
        await using var scope = new Scope();
        await scope.Service.StartAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => scope.Service.ApplyOptionsAsync(scope.Options with { ArchiveIntervalMinutes = 1 }));
        Assert.Equal(1440, scope.Service.CurrentOptions.ArchiveIntervalMinutes);
        Assert.True(scope.Writer.TryWrite(CreateEvent(1) with { TimestampUtc = DateTimeOffset.UtcNow.AddDays(-40) }));
        var archive = await scope.ArchiveAsync();
        Assert.Equal(ArchiveOutcome.Success, archive.Outcome);
        var rows = new List<HistoryEvent>();
        await foreach (var item in scope.Store.ReadRangeAsync(0, 1)) rows.Add(item);
        Assert.Empty(rows);
        var foreign = Path.Combine(scope.Directory, "foreign.zip");
        await File.WriteAllTextAsync(foreign, "user data");
        await scope.Service.ApplyOptionsAsync(scope.Options with { RetentionDays = 30 });
        Assert.Equal(ArchiveOutcome.NoNewEvents, (await scope.ArchiveAsync()).Outcome);
        Assert.Equal("user data", await File.ReadAllTextAsync(foreign));
    }

    [Fact]
    public async Task InsufficientSpaceKeepsEventsAndReportsError()
    {
        var files = new FaultFiles { AvailableBytes = 0 };
        await using var scope = new Scope(files);
        await scope.Service.StartAsync();
        await scope.Service.ApplyOptionsAsync(scope.Options with { MinimumFreeSpaceMb = 1 });
        Assert.True(scope.Writer.TryWrite(CreateEvent(1)));
        Assert.Equal(ArchiveOutcome.Error, (await scope.ArchiveAsync()).Outcome);
        Assert.Equal(0, await scope.Store.GetLastArchivedIdAsync());
        Assert.Equal(1, await scope.Store.GetMaximumIdAsync());
        Assert.Equal(scope.Directory, files.LastSpaceDirectory);
    }

    [Fact]
    public async Task ExpiredArchiveMetadataKeepsRangeCoveredAfterRetention()
    {
        var clock = new MutableClock();
        await using var scope = new Scope(clock: clock);
        await scope.Service.StartAsync();
        await scope.Service.ApplyOptionsAsync(scope.Options with { FileNameTemplate = "history.zip" });
        Assert.True(scope.Writer.TryWrite(CreateEvent(1) with { TimestampUtc = clock.UtcNow.AddDays(-40) }));
        var first = await scope.ArchiveAsync();
        Assert.Equal(ArchiveOutcome.Success, first.Outcome);
        var foreign = Path.Combine(scope.Directory, "foreign.zip");
        await File.WriteAllTextAsync(foreign, "user data");
        clock.UtcNow = clock.UtcNow.AddDays(40);
        await scope.Service.ApplyOptionsAsync(scope.Options with { FileNameTemplate = "history.zip", RetentionDays = 30 });
        Assert.Equal(ArchiveOutcome.NoNewEvents, (await scope.ArchiveAsync()).Outcome);
        Assert.False(File.Exists(first.Path));
        Assert.True(Assert.Single(await scope.Store.GetArchiveRunsAsync()).Expired);
        Assert.True(File.Exists(foreign));
        Assert.True(scope.Writer.TryWrite(CreateEvent(2)));
        var second = await scope.ArchiveAsync();
        Assert.Equal(ArchiveOutcome.Success, second.Outcome);
        Assert.NotEqual(first.Path, second.Path);
        Assert.EndsWith("history_001.zip", second.Path);
        Assert.Equal(2, (await HistoryZipArchive.VerifyAsync(second.Path!)).FirstEventId);
    }

    [Fact]
    public async Task MissingConfirmedArchivePreventsDeletionOfRemainingOriginals()
    {
        await using var scope = new Scope();
        await scope.Service.StartAsync();
        Assert.True(scope.Writer.TryWrite(CreateEvent(1)));
        var first = await scope.ArchiveAsync();
        File.Delete(first.Path!);
        Assert.True(scope.Writer.TryWrite(CreateEvent(2)));
        Assert.Equal(ArchiveOutcome.Error, (await scope.ArchiveAsync()).Outcome);
        await scope.Writer.FlushAsync();
        Assert.Equal(1, await scope.Store.GetLastArchivedIdAsync());
        Assert.Equal(2, await scope.Store.GetMaximumIdAsync());
    }

    [Fact]
    public async Task PersistedScheduleBaselineArchivesMissedIntervalOnRestart()
    {
        var clock = new MutableClock();
        await using var scope = new Scope(clock: clock);
        await scope.Service.StartAsync();
        Assert.NotNull(await scope.Store.GetMetadataAsync("lastScheduledCheckUtc"));
        Assert.True(scope.Writer.TryWrite(CreateEvent(1)));
        await scope.Writer.FlushAsync();
        await scope.Service.StopAsync();
        clock.UtcNow = clock.UtcNow.AddMinutes(16);
        var store = new SqliteHistoryEventStore(Path.Combine(scope.Directory, "events.db"));
        var writer = new HistoryEventWriter(store);
        var service = new HistoryArchiveService(store, writer, scope.Options with { Enabled = true, ArchiveIntervalMinutes = 15 },
            NullLogger<HistoryArchiveService>.Instance, clock);
        await service.StartAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (service.CurrentStatus.LastArchivedEventId < 1 || service.CurrentStatus.NextArchiveDueUtc != clock.UtcNow.AddMinutes(15))
                await Task.Delay(10, timeout.Token);
            Assert.Single(await store.GetArchiveRunsAsync());
            Assert.Equal(clock.UtcNow.AddMinutes(15), service.CurrentStatus.NextArchiveDueUtc);
        }
        finally { await service.StopAsync(); }
    }

    [Fact]
    public async Task InvalidArchiveConfigurationKeepsStorageRunningAndCanBeCorrected()
    {
        await using var scope = new Scope();
        var invalid = new HistoryArchiveService(scope.Store, scope.Writer,
            scope.Options with { FileNameTemplate = "../invalid.zip" }, NullLogger<HistoryArchiveService>.Instance);
        await invalid.StartAsync();
        try
        {
            Assert.True(invalid.CurrentStatus.StorageReady);
            Assert.Equal(HistoryArchiveState.Error, invalid.CurrentStatus.State);
            Assert.True(scope.Writer.TryWrite(CreateEvent(1)));
            await scope.Writer.FlushAsync();
            Assert.Equal(1, await scope.Store.GetMaximumIdAsync());
            await invalid.ApplyOptionsAsync(scope.Options);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            ArchiveResult result;
            do
            {
                result = await invalid.ArchiveNowAsync(timeout.Token);
                if (result.Outcome == ArchiveOutcome.AlreadyRunning) await Task.Delay(10, timeout.Token);
            } while (result.Outcome == ArchiveOutcome.AlreadyRunning);
            Assert.Equal(ArchiveOutcome.Success, result.Outcome);
        }
        finally { await invalid.StopAsync(); }
    }

    [Fact]
    public async Task RetentionFailureKeepsCommittedArchiveSuccessfulAndReportsSeparateError()
    {
        var clock = new MutableClock();
        var files = new FaultFiles();
        await using var scope = new Scope(files, clock);
        await scope.Service.StartAsync();
        Assert.True(scope.Writer.TryWrite(CreateEvent(1)));
        Assert.Equal(ArchiveOutcome.Success, (await scope.ArchiveAsync()).Outcome);
        clock.UtcNow = clock.UtcNow.AddDays(40);
        await scope.Service.ApplyOptionsAsync(scope.Options with { RetentionDays = 30 });
        files.FailDelete = true;
        Assert.True(scope.Writer.TryWrite(CreateEvent(2)));
        var result = await scope.ArchiveAsync();
        Assert.Equal(ArchiveOutcome.Success, result.Outcome);
        Assert.Equal(2, await scope.Store.GetLastArchivedIdAsync());
        Assert.Equal("HistoryRetention", scope.Service.CurrentStatus.LastErrorCode);
        Assert.Equal(result.Path, scope.Service.CurrentStatus.LastSuccessfulArchivePath);
        Assert.True(File.Exists(result.Path));
    }

    internal static HistoryEvent CreateEvent(int number) => new(Guid.NewGuid(), DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)),
        DateTimeOffset.UtcNow, "Получено", "equip.bucket.start", "StartCommand", "Карточка ковша", null, null, null,
        "true", "Mock", Guid.Parse("d2a04831-c12e-44ab-b108-e32a2b3f8983"));

    private sealed class FaultFiles : HistoryArchiveFileOperations
    {
        public bool FailCreate { get; set; }
        public bool FailAfterMove { get; set; }
        public bool FailDelete { get; set; }
        public bool PauseMove { get; set; }
        public long AvailableBytes { get; set; } = long.MaxValue;
        public string? LastSpaceDirectory { get; private set; }
        public ManualResetEventSlim MoveEntered { get; } = new();
        public ManualResetEventSlim AllowMove { get; } = new();
        public override FileStream Create(string path) => FailCreate ? throw new IOException("Test disk failure") : base.Create(path);
        public override void Published(string finalPath)
        {
            if (FailAfterMove) throw new IOException("Test failure after rename");
        }
        public override void Move(string temporaryPath, string finalPath)
        {
            MoveEntered.Set();
            if (PauseMove && !AllowMove.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Test move barrier");
            base.Move(temporaryPath, finalPath);
        }
        public override long GetAvailableBytes(string directory)
        {
            LastSpaceDirectory = directory;
            return AvailableBytes;
        }
        public override void Delete(string path)
        {
            if (FailDelete) throw new IOException("Test retention failure");
            base.Delete(path);
        }
    }

    private sealed class Scope : IDisposable, IAsyncDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "promflow-history-archive-tests-" + Guid.NewGuid().ToString("N"));
        public HistoryArchiveOptions Options { get; }
        public SqliteHistoryEventStore Store { get; }
        public HistoryEventWriter Writer { get; }
        public HistoryArchiveService Service { get; }
        public Scope(HistoryArchiveFileOperations? files = null, TimeProvider? clock = null)
        {
            System.IO.Directory.CreateDirectory(Directory);
            Options = new() { Enabled = false, ArchiveOnExit = false, Directory = Directory, MinimumFreeSpaceMb = 0 };
            Store = new(Path.Combine(Directory, "events.db"));
            Writer = new(Store);
            Service = new(Store, Writer, Options, NullLogger<HistoryArchiveService>.Instance, clock, files);
        }
        public async Task<ArchiveResult> ArchiveAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (true)
            {
                var result = await Service.ArchiveNowAsync(timeout.Token);
                if (result.Outcome != ArchiveOutcome.AlreadyRunning) return result;
                await Task.Delay(10, timeout.Token);
            }
        }
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
        public async ValueTask DisposeAsync()
        {
            await Service.StopAsync();
            Dispose();
        }
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
