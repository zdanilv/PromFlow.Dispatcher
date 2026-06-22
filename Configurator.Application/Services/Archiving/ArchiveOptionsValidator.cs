using System.Net;

namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Validates archive options without touching the filesystem.
/// </summary>
public sealed class ArchiveOptionsValidator
{
    private const int MaxDeviceIdLength = 64;
    private static readonly char[] InvalidPathChars =
        Path.GetInvalidPathChars().Concat(['"', '<', '>', '|', '?', '*']).Distinct().ToArray();

    public ArchiveValidationResult Validate(ArchiveOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<ArchiveValidationError>();

        ValidateIntervals(options, errors);
        ValidateCapacity(options, errors);
        ValidateRetention(options, errors);
        ValidatePartitionMode(options, errors);
        ValidatePath(options.BaseDirectory, nameof(options.BaseDirectory), errors);
        ValidatePath(options.ExportDirectory, nameof(options.ExportDirectory), errors);

        if (options.Enabled)
        {
            ValidateDeviceId(options.DeviceId, errors);
        }

        return new ArchiveValidationResult(errors);
    }

    private static void ValidateIntervals(ArchiveOptions options, List<ArchiveValidationError> errors)
    {
        if (options.LongTermSnapshotIntervalMs <= 0)
        {
            errors.Add(new ArchiveValidationError(
                "ArchiveIntervalInvalid",
                "Long-term snapshot interval must be greater than zero.",
                nameof(options.LongTermSnapshotIntervalMs)));
        }

        if (options.BatchFlushIntervalMs <= 0)
        {
            errors.Add(new ArchiveValidationError(
                "ArchiveIntervalInvalid",
                "Batch flush interval must be greater than zero.",
                nameof(options.BatchFlushIntervalMs)));
        }

        if (options.BusyTimeoutMs <= 0)
        {
            errors.Add(new ArchiveValidationError(
                "ArchiveIntervalInvalid",
                "Busy timeout must be greater than zero.",
                nameof(options.BusyTimeoutMs)));
        }
    }

    private static void ValidateCapacity(ArchiveOptions options, List<ArchiveValidationError> errors)
    {
        if (options.ChannelCapacity <= 0)
        {
            errors.Add(new ArchiveValidationError(
                "ArchiveCapacityInvalid",
                "Channel capacity must be greater than zero.",
                nameof(options.ChannelCapacity)));
        }

        if (options.BatchSize <= 0)
        {
            errors.Add(new ArchiveValidationError(
                "ArchiveCapacityInvalid",
                "Batch size must be greater than zero.",
                nameof(options.BatchSize)));
        }

        if (options.BatchSize > options.ChannelCapacity)
        {
            errors.Add(new ArchiveValidationError(
                "ArchiveCapacityInvalid",
                "Batch size must not exceed channel capacity.",
                nameof(options.BatchSize)));
        }
    }

    private static void ValidateRetention(ArchiveOptions options, List<ArchiveValidationError> errors)
    {
        if (options.HighResolutionRetentionHours <= 0)
        {
            errors.Add(new ArchiveValidationError(
                "ArchiveRetentionInvalid",
                "High-resolution retention must be greater than zero.",
                nameof(options.HighResolutionRetentionHours)));
        }

        if (options.LongTermRetentionDays <= 0)
        {
            errors.Add(new ArchiveValidationError(
                "ArchiveRetentionInvalid",
                "Long-term retention must be greater than zero.",
                nameof(options.LongTermRetentionDays)));
        }
    }

    private static void ValidatePartitionMode(ArchiveOptions options, List<ArchiveValidationError> errors)
    {
        if (!Enum.IsDefined(options.PartitionMode))
        {
            errors.Add(new ArchiveValidationError(
                "ArchivePartitionModeUnsupported",
                $"Archive partition mode '{options.PartitionMode}' is unsupported.",
                nameof(options.PartitionMode)));
        }
    }

    private static void ValidatePath(
        string? path,
        string propertyName,
        List<ArchiveValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (path.IndexOfAny(InvalidPathChars) >= 0)
        {
            errors.Add(new ArchiveValidationError(
                "ArchivePathInvalid",
                $"{propertyName} contains characters that are invalid in a path.",
                propertyName));
        }
    }

    private static void ValidateDeviceId(string? deviceId, List<ArchiveValidationError> errors)
    {
        var normalized = deviceId?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            errors.Add(new ArchiveValidationError(
                "ArchiveDeviceIdRequired",
                "Archive device ID is required when archive is enabled.",
                nameof(ArchiveOptions.DeviceId)));
            return;
        }

        if (normalized.Length > MaxDeviceIdLength
            || normalized.Any(static c => !IsDeviceIdCharacter(c))
            || normalized.Contains('\\')
            || normalized.Contains('/')
            || IPAddress.TryParse(normalized, out _))
        {
            errors.Add(new ArchiveValidationError(
                "ArchiveDeviceIdInvalid",
                "Archive device ID must be a stable logical ID, not an IP address or path.",
                nameof(ArchiveOptions.DeviceId)));
        }
    }

    private static bool IsDeviceIdCharacter(char value)
        => char.IsAsciiLetterOrDigit(value) || value is '.' or '_' or '-';
}
