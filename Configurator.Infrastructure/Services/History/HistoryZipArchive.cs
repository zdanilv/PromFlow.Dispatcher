using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Configurator.Application.Services.History;

namespace Configurator.Infrastructure.Services.History;

public sealed record HistoryArchiveManifest(
    int SchemaVersion, string Format, DateTimeOffset CreatedUtc,
    long FirstEventId, long LastEventId, long EventCount,
    DateTimeOffset FirstEventUtc, DateTimeOffset LastEventUtc,
    string EventsSha256, long EventsByteCount);

public static class HistoryZipArchive
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);
    private static readonly byte[] NewLine = [(byte)'\n'];

    public static async Task<HistoryArchiveManifest> WriteAsync(
        FileStream file, IAsyncEnumerable<HistoryEvent> events, DateTimeOffset createdUtc,
        long expectedFirstId, long expectedLastId, CancellationToken ct = default)
    {
        HistoryArchiveManifest manifest;
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            long count = 0, byteCount = 0, lastId = expectedFirstId - 1;
            DateTimeOffset firstUtc = default, lastUtc = default;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = zip.CreateEntry("events.jsonl", CompressionLevel.Optimal).Open())
            {
                await foreach (var item in events.WithCancellation(ct))
                {
                    if (item.Id != lastId + 1)
                        throw new InvalidDataException("Нарушена непрерывность ID истории.");
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(item with
                    {
                        TimestampUtc = item.TimestampUtc.ToUniversalTime(),
                        RecordedUtc = item.RecordedUtc.ToUniversalTime()
                    }, JsonOptions);
                    await output.WriteAsync(bytes, ct);
                    await output.WriteAsync(NewLine, ct);
                    hash.AppendData(bytes);
                    hash.AppendData(NewLine);
                    byteCount += bytes.Length + 1;
                    if (count == 0) firstUtc = item.TimestampUtc.ToUniversalTime();
                    lastUtc = item.TimestampUtc.ToUniversalTime();
                    lastId = item.Id;
                    count++;
                }
            }
            if (count == 0 || lastId != expectedLastId)
                throw new InvalidDataException("Диапазон истории неполон.");
            manifest = new(1, "PromFlow.History.JSONL", createdUtc.ToUniversalTime(), expectedFirstId, lastId,
                count, firstUtc, lastUtc, Convert.ToHexString(hash.GetHashAndReset()), byteCount);
            await using var manifestStream = zip.CreateEntry("manifest.json", CompressionLevel.Optimal).Open();
            await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, ct);
        }
        await file.FlushAsync(ct);
        file.Flush(flushToDisk: true);
        return manifest;
    }

    public static async Task<HistoryArchiveManifest> VerifyAsync(string path, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(path);
        if (zip.Entries.Count != 2 || zip.Entries.Count(e => e.FullName == "manifest.json") != 1 ||
            zip.Entries.Count(e => e.FullName == "events.jsonl") != 1)
            throw new InvalidDataException("Неверный состав архива истории.");
        await using var manifestStream = zip.GetEntry("manifest.json")!.Open();
        var manifest = await JsonSerializer.DeserializeAsync<HistoryArchiveManifest>(manifestStream, JsonOptions, ct)
            ?? throw new InvalidDataException("Отсутствует manifest истории.");
        if (manifest.SchemaVersion != 1 || manifest.Format != "PromFlow.History.JSONL" ||
            manifest.FirstEventId < 1 || manifest.LastEventId < manifest.FirstEventId ||
            manifest.EventCount != manifest.LastEventId - manifest.FirstEventId + 1)
            throw new InvalidDataException("Неверный диапазон manifest истории.");

        var entry = zip.GetEntry("events.jsonl")!;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long bytesRead = 0;
        await using (var input = entry.Open())
        {
            var buffer = new byte[65536];
            int length;
            while ((length = await input.ReadAsync(buffer, ct)) > 0)
            {
                hash.AppendData(buffer.AsSpan(0, length));
                bytesRead += length;
            }
        }
        if (bytesRead != manifest.EventsByteCount ||
            !string.Equals(Convert.ToHexString(hash.GetHashAndReset()), manifest.EventsSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Контрольная сумма архива истории не совпадает.");

        await using var eventStream = entry.Open();
        using var reader = new StreamReader(eventStream, new UTF8Encoding(false, true));
        long count = 0;
        DateTimeOffset firstUtc = default, lastUtc = default;
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            var item = JsonSerializer.Deserialize<HistoryEvent>(line, JsonOptions)
                ?? throw new InvalidDataException("Пустая запись истории.");
            if (item.Id != manifest.FirstEventId + count)
                throw new InvalidDataException("Неверный порядок записей истории.");
            if (count == 0) firstUtc = item.TimestampUtc;
            lastUtc = item.TimestampUtc;
            count++;
        }
        if (count != manifest.EventCount || firstUtc != manifest.FirstEventUtc || lastUtc != manifest.LastEventUtc)
            throw new InvalidDataException("Количество или время записей истории не совпадает с manifest.");
        return manifest;
    }
}
