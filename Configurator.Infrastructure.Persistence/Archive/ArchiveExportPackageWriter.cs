using Configurator.Application.Services.Archiving;
using Configurator.Infrastructure.Persistence.Common;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace Configurator.Infrastructure.Persistence.Archive;

public sealed class ArchiveExportPackageWriter
{
    private const string ManifestEntryName = "manifest.json";
    private const string CommandsEntryName = "commands.csv";
    private const string WritesEntryName = "modbus_writes.csv";
    private const string EventsEntryName = "events.csv";
    private const string SnapshotsEntryName = "snapshots.ndjson";
    private const string ChecksumsEntryName = "checksums.sha256";

    private static readonly string[] PackageEntries =
    [
        ManifestEntryName,
        CommandsEntryName,
        WritesEntryName,
        EventsEntryName,
        SnapshotsEntryName,
        ChecksumsEntryName
    ];

    private readonly IArchiveQueryService _queryService;
    private readonly ArchiveCsvWriter _csvWriter;
    private readonly ArchiveChecksum _checksum;
    private readonly IAppDataPathProvider _pathProvider;

    public ArchiveExportPackageWriter(
        IArchiveQueryService queryService,
        ArchiveCsvWriter csvWriter,
        ArchiveChecksum checksum,
        IAppDataPathProvider pathProvider)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _csvWriter = csvWriter ?? throw new ArgumentNullException(nameof(csvWriter));
        _checksum = checksum ?? throw new ArgumentNullException(nameof(checksum));
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public async Task<ArchiveOperationResult<ArchiveExportResult>> ExportAsync(
        ArchiveExportRequest request,
        ArchiveOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        var validation = ValidateRequest(request, options);
        if (!validation.Succeeded)
        {
            return ArchiveOperationResult<ArchiveExportResult>.Failure(
                validation.ErrorCode ?? ArchivePersistenceErrorCodes.ArchiveExportInvalid,
                validation.ErrorMessage ?? "Archive export request is invalid.",
                validation.ErrorDetails);
        }

        var destination = ResolveDestinationDirectory(request.ExportDirectory, options);
        var createdAtUtc = DateTimeOffset.UtcNow;
        var finalPath = ResolveFinalPath(destination, createdAtUtc, request.Query.DeviceId);
        var tempRoot = Path.Combine(destination, ".promflow-export-" + Guid.NewGuid().ToString("N"));
        var packageDirectory = Path.Combine(tempRoot, "package");
        var tempZipPath = Path.Combine(tempRoot, "export.zip.tmp");

        try
        {
            Directory.CreateDirectory(packageDirectory);
            var context = new ExportContext(request.Query, options);

            var commandCount = await WriteCommandsAsync(packageDirectory, context, cancellationToken).ConfigureAwait(false);
            var physicalWriteCount = await WritePhysicalWritesAsync(packageDirectory, context, cancellationToken).ConfigureAwait(false);
            var runtimeEventCount = await WriteRuntimeEventsAsync(packageDirectory, context, cancellationToken).ConfigureAwait(false);
            var snapshotMetadataCount = await WriteSnapshotMetadataAsync(packageDirectory, context, cancellationToken).ConfigureAwait(false);

            var totalRecords = checked(commandCount + physicalWriteCount + runtimeEventCount + snapshotMetadataCount);
            if (totalRecords > options.ExportMaxRecords)
            {
                return ArchiveOperationResult<ArchiveExportResult>.Failure(
                    ArchivePersistenceErrorCodes.ArchiveExportLimitExceeded,
                    "Archive export record count exceeds configured maximum.",
                    totalRecords.ToString(CultureInfo.InvariantCulture));
            }

            var checksums = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CommandsEntryName] = await _checksum.ComputeSha256Async(Path.Combine(packageDirectory, CommandsEntryName), cancellationToken).ConfigureAwait(false),
                [WritesEntryName] = await _checksum.ComputeSha256Async(Path.Combine(packageDirectory, WritesEntryName), cancellationToken).ConfigureAwait(false),
                [EventsEntryName] = await _checksum.ComputeSha256Async(Path.Combine(packageDirectory, EventsEntryName), cancellationToken).ConfigureAwait(false),
                [SnapshotsEntryName] = await _checksum.ComputeSha256Async(Path.Combine(packageDirectory, SnapshotsEntryName), cancellationToken).ConfigureAwait(false)
            };

            var manifestPath = Path.Combine(packageDirectory, ManifestEntryName);
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(
                    new
                    {
                        createdAtUtc,
                        format = request.Format.ToString(),
                        query = CreateManifestQuery(request.Query),
                        counts = new
                        {
                            commands = commandCount,
                            modbusWrites = physicalWriteCount,
                            runtimeEvents = runtimeEventCount,
                            snapshotMetadata = snapshotMetadataCount
                        },
                        entries = PackageEntries,
                        sha256ByEntryName = checksums
                    },
                    new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken).ConfigureAwait(false);
            checksums[ManifestEntryName] = await _checksum.ComputeSha256Async(manifestPath, cancellationToken).ConfigureAwait(false);

            await WriteChecksumsAsync(packageDirectory, checksums, cancellationToken).ConfigureAwait(false);
            await CreateZipAsync(packageDirectory, tempZipPath, cancellationToken).ConfigureAwait(false);
            File.Move(tempZipPath, finalPath);

            return ArchiveOperationResult<ArchiveExportResult>.Success(new ArchiveExportResult(
                finalPath,
                createdAtUtc,
                commandCount,
                physicalWriteCount,
                runtimeEventCount,
                snapshotMetadataCount,
                new ReadOnlyDictionary<string, string>(checksums)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArchiveExportLimitException ex)
        {
            return ArchiveOperationResult<ArchiveExportResult>.Failure(
                ArchivePersistenceErrorCodes.ArchiveExportLimitExceeded,
                "Archive export record count exceeds configured maximum.",
                ex.Message);
        }
        catch (ArchiveExportException ex)
        {
            return ArchiveOperationResult<ArchiveExportResult>.Failure(
                ex.Code,
                ex.Message,
                ex.Details);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return ArchiveOperationResult<ArchiveExportResult>.Failure(
                ArchivePersistenceErrorCodes.ArchiveExportFailed,
                "Archive export failed.",
                ex.Message);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private async Task<long> WriteCommandsAsync(
        string packageDirectory,
        ExportContext context,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(packageDirectory, CommandsEntryName);
        await using var stream = CreateTextFile(path);
        await using var writer = new StreamWriter(stream);
        await _csvWriter.WriteHeaderAsync(
            writer,
            [
                "command_id", "correlation_id", "requested_at_utc", "completed_at_utc",
                "session_id", "user_id", "username", "device_id", "signal_id", "value_type",
                "requested_value_canonical", "write_mode", "result", "error_code", "error_message",
                "confirmation_status", "confirmed_at_utc", "archive_schema_version"
            ],
            cancellationToken).ConfigureAwait(false);

        if (!ShouldExportKind(context.Query, ArchiveRecordKind.EquipmentCommandAudit))
        {
            return 0;
        }

        return await WritePagedAsync(
            context,
            pageQuery => _queryService.QueryEquipmentCommandsAsync(pageQuery, cancellationToken),
            record => _csvWriter.WriteCommandAsync(writer, record, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<long> WritePhysicalWritesAsync(
        string packageDirectory,
        ExportContext context,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(packageDirectory, WritesEntryName);
        await using var stream = CreateTextFile(path);
        await using var writer = new StreamWriter(stream);
        await _csvWriter.WriteHeaderAsync(
            writer,
            [
                "write_id", "command_id", "attempted_at_utc", "completed_at_utc", "runtime_role",
                "area", "address", "quantity", "payload_hex", "succeeded", "error_code",
                "error_message", "archive_schema_version"
            ],
            cancellationToken).ConfigureAwait(false);

        if (!ShouldExportKind(context.Query, ArchiveRecordKind.PhysicalModbusWriteAudit))
        {
            return 0;
        }

        return await WritePagedAsync(
            context,
            pageQuery => _queryService.QueryPhysicalWritesAsync(pageQuery, cancellationToken),
            record => _csvWriter.WritePhysicalWriteAsync(writer, record, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<long> WriteRuntimeEventsAsync(
        string packageDirectory,
        ExportContext context,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(packageDirectory, EventsEntryName);
        await using var stream = CreateTextFile(path);
        await using var writer = new StreamWriter(stream);
        await _csvWriter.WriteHeaderAsync(
            writer,
            ["id", "occurred_at_utc", "device_id", "event_type", "severity", "message", "details_json"],
            cancellationToken).ConfigureAwait(false);

        if (context.Query.RecordKind is not null && context.Query.RecordKind != ArchiveRecordKind.ModbusStatus)
        {
            return 0;
        }

        return await WritePagedAsync(
            context,
            pageQuery => _queryService.QueryRuntimeEventsAsync(pageQuery, cancellationToken),
            record => _csvWriter.WriteRuntimeEventAsync(writer, record, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<long> WriteSnapshotMetadataAsync(
        string packageDirectory,
        ExportContext context,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(packageDirectory, SnapshotsEntryName);
        await using var stream = CreateTextFile(path);
        await using var writer = new StreamWriter(stream);

        if (!ShouldExportKind(context.Query, ArchiveRecordKind.RawModbusSnapshot))
        {
            return 0;
        }

        return await WritePagedAsync(
            context,
            pageQuery => _queryService.QueryRawSnapshotsAsync(pageQuery, cancellationToken),
            record =>
            {
                var json = JsonSerializer.Serialize(new
                {
                    id = record.Id,
                    deviceId = record.DeviceId,
                    runtimeRole = record.Role.ToString(),
                    sequenceNumber = record.SequenceNumber,
                    capturedAtUtc = record.CapturedAtUtc,
                    coilStartAddress = record.CoilStartAddress,
                    holdingRegisterStartAddress = record.HoldingRegisterStartAddress,
                    coilCount = record.Coils.Count,
                    holdingRegisterCount = record.HoldingRegisters.Count,
                    configurationHash = record.ConfigurationHash,
                    resolution = record.Resolution.ToString(),
                    archiveSchemaVersion = record.SchemaVersion
                });
                return writer.WriteLineAsync(json.AsMemory(), cancellationToken);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<long> WritePagedAsync<T>(
        ExportContext context,
        Func<ArchiveQuery, Task<ArchiveOperationResult<ArchivePage<T>>>> queryPageAsync,
        Func<T, Task> writeRecordAsync,
        CancellationToken cancellationToken)
    {
        var pageNumber = 1;
        var count = 0L;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageQuery = CreatePageQuery(context.Query, pageNumber, context.PageSize);
            var pageOutcome = await queryPageAsync(pageQuery).ConfigureAwait(false);
            if (!pageOutcome.Succeeded || pageOutcome.Value is null)
            {
                throw new ArchiveExportException(
                    pageOutcome.ErrorCode ?? ArchivePersistenceErrorCodes.ArchiveExportFailed,
                    pageOutcome.ErrorMessage ?? "Archive export query failed.",
                    pageOutcome.ErrorDetails);
            }

            foreach (var item in pageOutcome.Value.Items)
            {
                count++;
                if (count > context.MaxRecords)
                {
                    throw new ArchiveExportLimitException(count.ToString(CultureInfo.InvariantCulture));
                }

                await writeRecordAsync(item).ConfigureAwait(false);
            }

            if (!pageOutcome.Value.HasMore)
            {
                return count;
            }

            pageNumber++;
        }
    }

    private static ArchiveOperationResult ValidateRequest(ArchiveExportRequest request, ArchiveOptions options)
    {
        if (request.Format != ArchiveExportFormat.ZipPackage)
        {
            return ArchiveOperationResult.Failure(
                ArchivePersistenceErrorCodes.ArchiveExportInvalid,
                "Archive export format is unsupported.",
                request.Format.ToString());
        }

        if (request.Query.FromUtc is null || request.Query.ToUtc is null)
        {
            return ArchiveOperationResult.Failure(
                ArchivePersistenceErrorCodes.ArchiveExportInvalid,
                "Archive export requires a bounded UTC range.");
        }

        var range = request.Query.ToUtc.Value - request.Query.FromUtc.Value;
        if (range < TimeSpan.Zero || range.TotalDays > options.ExportMaxRangeDays)
        {
            return ArchiveOperationResult.Failure(
                ArchivePersistenceErrorCodes.ArchiveExportInvalid,
                "Archive export range exceeds configured maximum.",
                options.ExportMaxRangeDays.ToString(CultureInfo.InvariantCulture));
        }

        if (request.Query.PageSize > options.QueryMaxPageSize)
        {
            return ArchiveOperationResult.Failure(
                ArchivePersistenceErrorCodes.ArchiveExportInvalid,
                "Archive export query page size exceeds configured maximum.",
                options.QueryMaxPageSize.ToString(CultureInfo.InvariantCulture));
        }

        return ArchiveOperationResult.Success();
    }

    private string ResolveDestinationDirectory(string? requestedDirectory, ArchiveOptions options)
    {
        var directory = string.IsNullOrWhiteSpace(requestedDirectory)
            ? _pathProvider.GetArchiveExportDirectory(options)
            : requestedDirectory.Trim();
        var fullPath = Path.GetFullPath(directory);
        Directory.CreateDirectory(fullPath);

        return fullPath;
    }

    private static string ResolveFinalPath(
        string destination,
        DateTimeOffset timestampUtc,
        string? deviceId)
    {
        var safeDevice = string.IsNullOrWhiteSpace(deviceId)
            ? "archive"
            : SanitizeFileNamePart(deviceId);
        var stamp = timestampUtc.ToUniversalTime().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

        for (var index = 0; index < 1000; index++)
        {
            var suffix = index == 0 ? string.Empty : "-" + index.ToString(CultureInfo.InvariantCulture);
            var candidate = Path.Combine(destination, $"promflow-export-{safeDevice}-{stamp}{suffix}.zip");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("Archive export path could not be made unique.");
    }

    private static async Task WriteChecksumsAsync(
        string packageDirectory,
        IReadOnlyDictionary<string, string> checksums,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(packageDirectory, ChecksumsEntryName);
        await using var stream = CreateTextFile(path);
        await using var writer = new StreamWriter(stream);
        foreach (var pair in checksums.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            await writer
                .WriteLineAsync($"{pair.Value}  {pair.Key}".AsMemory(), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task CreateZipAsync(
        string packageDirectory,
        string zipPath,
        CancellationToken cancellationToken)
    {
        await using var zipStream = new FileStream(
            zipPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);

        foreach (var entryName in PackageEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = Path.Combine(packageDirectory, entryName);
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            await using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                useAsync: true);
            await using var target = entry.Open();
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }
    }

    private static ArchiveQuery CreatePageQuery(ArchiveQuery source, int pageNumber, int pageSize)
    {
        var requestedOutcome = source switch { { Result: var value } => value };
        return new ArchiveQuery(
            source.FromUtc,
            source.ToUtc,
            source.DeviceId,
            source.Role,
            source.RecordKind,
            source.EventType,
            source.SignalId,
            source.UserId,
            requestedOutcome,
            pageNumber,
            pageSize,
            source.SortDirection);
    }

    private static object CreateManifestQuery(ArchiveQuery source)
    {
        var requestedOutcome = source switch { { Result: var value } => value };
        return new
        {
            source.FromUtc,
            source.ToUtc,
            source.DeviceId,
            role = source.Role?.ToString(),
            recordKind = source.RecordKind?.ToString(),
            source.EventType,
            source.SignalId,
            source.UserId,
            result = requestedOutcome,
            source.SortDirection
        };
    }

    private static bool ShouldExportKind(ArchiveQuery query, ArchiveRecordKind kind)
        => query.RecordKind is null || query.RecordKind == kind;

    private static FileStream CreateTextFile(string path)
        => new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);

    private static string SanitizeFileNamePart(string value)
    {
        var sanitized = new string(value.Trim().Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' ? character : '_').ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "archive" : sanitized;
    }

    private static void TryDeleteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record ExportContext(ArchiveQuery Query, ArchiveOptions Options)
    {
        public int PageSize { get; } = Math.Max(1, Options.QueryMaxPageSize);

        public int MaxRecords { get; } = Math.Max(1, Options.ExportMaxRecords);
    }

    private sealed class ArchiveExportException(string code, string message, string? details = null) : Exception(message)
    {
        public string Code { get; } = code;

        public string? Details { get; } = details;
    }

    private sealed class ArchiveExportLimitException(string message) : Exception(message);
}
