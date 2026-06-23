using Configurator.Application.Services.Archiving;
using Configurator.Infrastructure.Persistence.Common;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Configurator.Infrastructure.Persistence.Archive;

public sealed partial class ArchivePartitionCatalog
{
    private const int MaxSanitizedDeviceIdLength = 64;
    private readonly IAppDataPathProvider _appDataPathProvider;

    public ArchivePartitionCatalog(IAppDataPathProvider appDataPathProvider)
    {
        _appDataPathProvider = appDataPathProvider ?? throw new ArgumentNullException(nameof(appDataPathProvider));
    }

    public IReadOnlyList<ArchivePartitionInfo> GetExistingPartitions(
        ArchiveOptions options,
        ArchiveQuery? query = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var baseDirectory = _appDataPathProvider.GetArchiveBaseDirectory(options);
        if (!Directory.Exists(baseDirectory))
        {
            return Array.Empty<ArchivePartitionInfo>();
        }

        var requestedDeviceId = string.IsNullOrWhiteSpace(query?.DeviceId)
            ? null
            : query.DeviceId.Trim();
        var requestedSanitizedDeviceId = requestedDeviceId is null
            ? null
            : SanitizeDeviceId(requestedDeviceId);
        var partitions = new List<ArchivePartitionInfo>();

        foreach (var file in Directory.EnumerateFiles(baseDirectory, "promflow-*.sqlite", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(file);
            var match = PartitionFileNamePattern().Match(fileName);
            if (!match.Success)
            {
                continue;
            }

            var sanitizedDeviceId = match.Groups["device"].Value;
            if (requestedSanitizedDeviceId is not null
                && !string.Equals(sanitizedDeviceId, requestedSanitizedDeviceId, StringComparison.Ordinal))
            {
                continue;
            }

            var year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
            var month = int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
            var startUtc = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
            var endExclusiveUtc = startUtc.AddMonths(1);

            if (!OverlapsQueryRange(startUtc, endExclusiveUtc, query))
            {
                continue;
            }

            partitions.Add(new ArchivePartitionInfo(
                requestedDeviceId ?? sanitizedDeviceId,
                sanitizedDeviceId,
                year,
                month,
                startUtc,
                endExclusiveUtc,
                Path.GetFullPath(file),
                ArchivePartitionAccess.ReadOnly));
        }

        return partitions
            .OrderBy(partition => partition.StartUtc)
            .ThenBy(partition => partition.DatabasePath, StringComparer.Ordinal)
            .ToArray();
    }

    public ArchivePartitionInfo GetCurrentWritablePartition(ArchiveOptions options, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(options);

        var timestamp = nowUtc.ToUniversalTime();
        var startUtc = new DateTimeOffset(timestamp.Year, timestamp.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var deviceId = string.IsNullOrWhiteSpace(options.DeviceId)
            ? throw new ArgumentException("Archive device ID is required.", nameof(options.DeviceId))
            : options.DeviceId.Trim();
        var sanitizedDeviceId = SanitizeDeviceId(deviceId);
        var baseDirectory = _appDataPathProvider.GetArchiveBaseDirectory(options);
        var path = Path.Combine(
            Path.GetFullPath(baseDirectory),
            FormattableString.Invariant($"promflow-{sanitizedDeviceId}-{startUtc.Year:D4}-{startUtc.Month:D2}.sqlite"));

        return new ArchivePartitionInfo(
            deviceId,
            sanitizedDeviceId,
            startUtc.Year,
            startUtc.Month,
            startUtc,
            startUtc.AddMonths(1),
            path,
            ArchivePartitionAccess.ReadWrite);
    }

    public static string GetWalPath(string databasePath)
        => databasePath + "-wal";

    public static string GetSharedMemoryPath(string databasePath)
        => databasePath + "-shm";

    private static bool OverlapsQueryRange(
        DateTimeOffset partitionStartUtc,
        DateTimeOffset partitionEndExclusiveUtc,
        ArchiveQuery? query)
    {
        if (query?.FromUtc is DateTimeOffset fromUtc && partitionEndExclusiveUtc <= fromUtc)
        {
            return false;
        }

        if (query?.ToUtc is DateTimeOffset toUtc && partitionStartUtc > toUtc)
        {
            return false;
        }

        return true;
    }

    private static string SanitizeDeviceId(string deviceId)
    {
        var length = Math.Min(deviceId.Length, MaxSanitizedDeviceIdLength);
        var sanitized = new char[length];

        for (var index = 0; index < length; index++)
        {
            var value = deviceId[index];
            sanitized[index] = char.IsAsciiLetterOrDigit(value) || value is '.' or '_' or '-'
                ? value
                : '_';
        }

        return new string(sanitized);
    }

    [GeneratedRegex("^promflow-(?<device>.+)-(?<year>\\d{4})-(?<month>\\d{2})\\.sqlite$", RegexOptions.CultureInvariant)]
    private static partial Regex PartitionFileNamePattern();
}
