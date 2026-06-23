using Configurator.Application.Services.Archiving;
using Configurator.Infrastructure.Persistence.Common;
using Configurator.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace Configurator.Infrastructure.Persistence.Archive;

public sealed class ArchiveBackupPackageWriter
{
    private const string ManifestEntryName = "manifest.json";
    private const string ChecksumsEntryName = "checksums.sha256";
    private const string PartitionEntryPrefix = "partitions/";

    private readonly ArchivePartitionCatalog _partitionCatalog;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqlitePragmaInitializer _pragmaInitializer;
    private readonly ArchiveChecksum _checksum;

    public ArchiveBackupPackageWriter(
        ArchivePartitionCatalog partitionCatalog,
        SqliteConnectionFactory connectionFactory,
        SqlitePragmaInitializer pragmaInitializer,
        ArchiveChecksum checksum)
    {
        _partitionCatalog = partitionCatalog ?? throw new ArgumentNullException(nameof(partitionCatalog));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _pragmaInitializer = pragmaInitializer ?? throw new ArgumentNullException(nameof(pragmaInitializer));
        _checksum = checksum ?? throw new ArgumentNullException(nameof(checksum));
    }

    public async Task<ArchiveOperationResult<ArchiveBackupResult>> CreateBackupAsync(
        string destinationDirectory,
        ArchiveOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            return ArchiveOperationResult<ArchiveBackupResult>.Failure(
                ArchivePersistenceErrorCodes.ArchiveBackupInvalid,
                "Archive backup destination directory is required.");
        }

        var createdAtUtc = DateTimeOffset.UtcNow;
        var destination = Path.GetFullPath(destinationDirectory.Trim());
        var finalPath = ResolveFinalPath(destination, createdAtUtc);
        var tempRoot = Path.Combine(destination, ".promflow-backup-" + Guid.NewGuid().ToString("N"));
        var packageDirectory = Path.Combine(tempRoot, "package");
        var partitionDirectory = Path.Combine(packageDirectory, "partitions");
        var tempZipPath = Path.Combine(tempRoot, "backup.zip.tmp");

        try
        {
            Directory.CreateDirectory(partitionDirectory);
            var partitions = _partitionCatalog.GetExistingPartitions(options);
            var results = new List<ArchiveBackupPartitionResult>(partitions.Count);
            var checksums = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var partition in partitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(partition.DatabasePath);
                var entryName = PartitionEntryPrefix + fileName;
                var backupPath = Path.Combine(partitionDirectory, fileName);
                var partitionOutcome = await BackupPartitionAsync(
                    partition.DatabasePath,
                    backupPath,
                    entryName,
                    options,
                    cancellationToken).ConfigureAwait(false);
                if (!partitionOutcome.Succeeded || partitionOutcome.Value is null)
                {
                    return ArchiveOperationResult<ArchiveBackupResult>.Failure(
                        partitionOutcome.ErrorCode ?? ArchivePersistenceErrorCodes.ArchiveBackupFailed,
                        partitionOutcome.ErrorMessage ?? "Archive partition backup failed.",
                        partitionOutcome.ErrorDetails);
                }

                results.Add(partitionOutcome.Value);
                checksums[entryName] = partitionOutcome.Value.Sha256;
            }

            var manifestPath = Path.Combine(packageDirectory, ManifestEntryName);
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(
                    new
                    {
                        createdAtUtc,
                        partitions = results.Select(partition => new
                        {
                            partition.SourcePath,
                            partition.EntryName,
                            partition.LengthBytes,
                            partition.Sha256,
                            partition.QuickCheckPassed
                        }).ToArray()
                    },
                    new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken).ConfigureAwait(false);
            checksums[ManifestEntryName] = await _checksum.ComputeSha256Async(manifestPath, cancellationToken).ConfigureAwait(false);

            await WriteChecksumsAsync(packageDirectory, checksums, cancellationToken).ConfigureAwait(false);
            await CreateZipAsync(packageDirectory, tempZipPath, checksums.Keys.Append(ChecksumsEntryName).ToArray(), cancellationToken)
                .ConfigureAwait(false);
            File.Move(tempZipPath, finalPath);

            return ArchiveOperationResult<ArchiveBackupResult>.Success(new ArchiveBackupResult(
                finalPath,
                createdAtUtc,
                results,
                new ReadOnlyDictionary<string, string>(checksums)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArchiveBackupException ex)
        {
            return ArchiveOperationResult<ArchiveBackupResult>.Failure(ex.Code, ex.Message, ex.Details);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return ArchiveOperationResult<ArchiveBackupResult>.Failure(
                ArchivePersistenceErrorCodes.ArchiveBackupFailed,
                "Archive backup failed.",
                ex.Message);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private async Task<ArchiveOperationResult<ArchiveBackupPartitionResult>> BackupPartitionAsync(
        string sourcePath,
        string destinationPath,
        string entryName,
        ArchiveOptions options,
        CancellationToken cancellationToken)
    {
        var sourceOpen = await _connectionFactory.OpenReadOnlyAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        if (!sourceOpen.Succeeded || sourceOpen.Value is null)
        {
            if (sourceOpen.ErrorCode == ArchivePersistenceErrorCodes.ArchivePartitionMissing)
            {
                return ArchiveOperationResult<ArchiveBackupPartitionResult>.Failure(
                    ArchivePersistenceErrorCodes.ArchivePartitionMissing,
                    "Archive partition disappeared during backup.",
                    sourcePath);
            }

            return ArchiveOperationResult<ArchiveBackupPartitionResult>.Failure(
                sourceOpen.ErrorCode ?? ArchivePersistenceErrorCodes.ArchiveBackupFailed,
                sourceOpen.ErrorMessage ?? "Archive partition could not be opened for backup.",
                sourceOpen.ErrorDetails ?? sourcePath);
        }

        await using var source = sourceOpen.Value;
        var pragma = await _pragmaInitializer.ApplyReadOnlyAsync(source, options.BusyTimeoutMs, cancellationToken).ConfigureAwait(false);
        if (!pragma.Succeeded)
        {
            return ArchiveOperationResult<ArchiveBackupPartitionResult>.Failure(
                pragma.ErrorCode ?? ArchivePersistenceErrorCodes.ArchiveBackupFailed,
                pragma.ErrorMessage ?? "Archive backup read-only PRAGMA failed.",
                pragma.ErrorDetails ?? sourcePath);
        }

        await using (var destination = OpenBackupDestination(destinationPath))
        {
            source.BackupDatabase(destination);
        }

        var quickCheck = await RunQuickCheckAsync(destinationPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(quickCheck, "ok", StringComparison.OrdinalIgnoreCase))
        {
            return ArchiveOperationResult<ArchiveBackupPartitionResult>.Failure(
                ArchivePersistenceErrorCodes.ArchiveBackupIntegrityFailed,
                "Archive backup quick_check failed.",
                quickCheck);
        }

        var sha = await _checksum.ComputeSha256Async(destinationPath, cancellationToken).ConfigureAwait(false);
        var length = new FileInfo(destinationPath).Length;

        return ArchiveOperationResult<ArchiveBackupPartitionResult>.Success(new ArchiveBackupPartitionResult(
            sourcePath,
            entryName,
            length,
            sha,
            quickCheckPassed: true));
    }

    private static SqliteConnection OpenBackupDestination(string destinationPath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
        var connection = new SqliteConnection(connectionString);
        connection.Open();

        return connection;
    }

    private static async Task<string> RunQuickCheckAsync(string path, CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";

        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture)
            ?? string.Empty;
    }

    private static string ResolveFinalPath(string destination, DateTimeOffset timestampUtc)
    {
        Directory.CreateDirectory(destination);
        var stamp = timestampUtc.ToUniversalTime().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        for (var index = 0; index < 1000; index++)
        {
            var suffix = index == 0 ? string.Empty : "-" + index.ToString(CultureInfo.InvariantCulture);
            var candidate = Path.Combine(destination, $"promflow-backup-{stamp}{suffix}.zip");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("Archive backup path could not be made unique.");
    }

    private static async Task WriteChecksumsAsync(
        string packageDirectory,
        IReadOnlyDictionary<string, string> checksums,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(packageDirectory, ChecksumsEntryName);
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
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
        IReadOnlyList<string> entryNames,
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

        foreach (var entryName in entryNames.OrderBy(name => name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = Path.Combine(packageDirectory, entryName.Replace('/', Path.DirectorySeparatorChar));
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

    private sealed class ArchiveBackupException(string code, string message, string? details = null) : Exception(message)
    {
        public string Code { get; } = code;

        public string? Details { get; } = details;
    }
}
