using Configurator.Application.Services.Archiving;
using Configurator.Infrastructure.Persistence.Common;

namespace Configurator.Infrastructure.Persistence.Archive;

public sealed class ArchivePartitionResolver
{
    private const int MaxSanitizedDeviceIdLength = 64;
    private readonly IAppDataPathProvider _appDataPathProvider;

    public ArchivePartitionResolver(IAppDataPathProvider appDataPathProvider)
    {
        _appDataPathProvider = appDataPathProvider ?? throw new ArgumentNullException(nameof(appDataPathProvider));
    }

    public ArchivePartitionInfo GetWritablePartition(ArchiveOptions options, DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureMonthlyPartitionMode(options);

        var timestamp = timestampUtc.ToUniversalTime();
        var start = GetMonthStartUtc(timestamp);

        return CreatePartition(options, start, ArchivePartitionAccess.ReadWrite);
    }

    public IReadOnlyList<ArchivePartitionInfo> GetPartitionsForRange(
        ArchiveOptions options,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureMonthlyPartitionMode(options);

        var from = fromUtc.ToUniversalTime();
        var to = toUtc.ToUniversalTime();
        if (from > to)
        {
            throw new ArgumentException("Archive partition range start must be earlier than range end.", nameof(fromUtc));
        }

        var current = GetMonthStartUtc(from);
        var final = GetMonthStartUtc(to);
        var partitions = new List<ArchivePartitionInfo>();

        while (current <= final)
        {
            partitions.Add(CreatePartition(options, current, ArchivePartitionAccess.ReadOnly));
            current = current.AddMonths(1);
        }

        return partitions.AsReadOnly();
    }

    private ArchivePartitionInfo CreatePartition(
        ArchiveOptions options,
        DateTimeOffset startUtc,
        ArchivePartitionAccess access)
    {
        var deviceId = GetDeviceId(options);
        var sanitizedDeviceId = SanitizeDeviceId(deviceId);
        var fileName = FormattableString.Invariant(
            $"promflow-{sanitizedDeviceId}-{startUtc.Year:D4}-{startUtc.Month:D2}.sqlite");
        var baseDirectory = _appDataPathProvider.GetArchiveBaseDirectory(options);
        var databasePath = ResolveUnderBaseDirectory(baseDirectory, fileName);

        return new ArchivePartitionInfo(
            deviceId,
            sanitizedDeviceId,
            startUtc.Year,
            startUtc.Month,
            startUtc,
            startUtc.AddMonths(1),
            databasePath,
            access);
    }

    private static void EnsureMonthlyPartitionMode(ArchiveOptions options)
    {
        if (options.PartitionMode != ArchivePartitionMode.Monthly)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.PartitionMode),
                options.PartitionMode,
                "Archive partition mode is unsupported.");
        }
    }

    private static string GetDeviceId(ArchiveOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DeviceId))
        {
            throw new ArgumentException("Archive device ID is required.", nameof(options.DeviceId));
        }

        return options.DeviceId.Trim();
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

    private static DateTimeOffset GetMonthStartUtc(DateTimeOffset value)
        => new(value.Year, value.Month, 1, 0, 0, 0, TimeSpan.Zero);

    private static string ResolveUnderBaseDirectory(string baseDirectory, string fileName)
    {
        var fullBaseDirectory = Path.GetFullPath(baseDirectory);
        var databasePath = Path.GetFullPath(Path.Combine(fullBaseDirectory, fileName));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!databasePath.StartsWith(EnsureTrailingDirectorySeparator(fullBaseDirectory), comparison))
        {
            throw new ArgumentException("Archive partition path must remain under archive base directory.", nameof(baseDirectory));
        }

        return databasePath;
    }

    private static string EnsureTrailingDirectorySeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
}
