using System.Diagnostics;
using Configurator.Application.Services.History;
using Configurator.Infrastructure.Services.History;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Configurator.Tests.Unit.History;

public sealed class HistoryArchiveBenchmarkTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(10_000)]
    [InlineData(100_000)]
    [InlineData(1_000_000)]
    [Trait("Category", "HistoryBenchmark")]
    public async Task MeasureRepresentativeArchive(int count)
    {
        var directory = Path.Combine(Path.GetTempPath(), "PromFlow_History_Benchmark_" + count + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, "events.db");
        var store = new SqliteHistoryEventStore(database);
        var writer = new HistoryEventWriter(store);
        var options = new HistoryArchiveOptions { Directory = directory, Enabled = false, ArchiveOnExit = false, MinimumFreeSpaceMb = 0 };
        var service = new HistoryArchiveService(store, writer, options, NullLogger<HistoryArchiveService>.Instance);
        await service.StartAsync();
        try
        {
            var start = new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero);
            var session = Guid.NewGuid();
            var batch = new List<HistoryEvent>(200);
            for (var i = 0; i < count; i++)
            {
                batch.Add(new(Guid.NewGuid(), start.AddMilliseconds(i * 100), start.AddMilliseconds(i * 100 + 15),
                    i % 5 == 0 ? "Отправлено" : "Получено", "equip.bucket." + i % 32,
                    i % 5 == 0 ? "StartCommand" : "PowerFeedback", "Карточка ковша", "HoldingRegister", i % 64,
                    i % 16, (i % 2 == 0).ToString().ToLowerInvariant(), "Modbus", session));
                if (batch.Count == 200)
                {
                    await store.AppendBatchAsync(batch);
                    batch.Clear();
                }
            }
            if (batch.Count != 0) await store.AppendBatchAsync(batch);
            var dbBefore = Sizes(database);
            var stopwatch = Stopwatch.StartNew();
            var result = await service.ArchiveNowAsync();
            stopwatch.Stop();
            Assert.Equal(ArchiveOutcome.Success, result.Outcome);
            var manifest = await HistoryZipArchive.VerifyAsync(result.Path!);
            Assert.Equal(count, manifest.EventCount);
            var zipBytes = new FileInfo(result.Path!).Length;
            var dbAfter = Sizes(database);
            await store.CheckpointAsync();
            output.WriteLine("events={0}; jsonlBytes={1}; zipBytes={2}; zipBytesPerEvent={3:F3}; compressionRatio={4:F3}; creationMs={5}; peakWorkingSetBytes={6}",
                count, manifest.EventsByteCount, zipBytes, (double)zipBytes / count, (double)manifest.EventsByteCount / zipBytes,
                stopwatch.ElapsedMilliseconds, Process.GetCurrentProcess().PeakWorkingSet64);
            output.WriteLine("db/wal/shm before archive={0}; after archive={1}; after checkpoint={2}", dbBefore, dbAfter, Sizes(database));
            if (count == 10_000 && Environment.GetEnvironmentVariable("PROMFLOW_KEEP_HISTORY_BENCHMARK_SAMPLE") == "1")
            {
                var sample = Path.Combine(Path.GetTempPath(), "PromFlow_History_Benchmark_Sample_" + Guid.NewGuid().ToString("N") + ".zip");
                File.Copy(result.Path!, sample);
                output.WriteLine("sampleArchive={0}", sample);
            }
        }
        finally
        {
            await service.StopAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string Sizes(string database) => string.Join("/", new[] { database, database + "-wal", database + "-shm" }
        .Select(path => File.Exists(path) ? new FileInfo(path).Length : 0));
}
