namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Priority-tagged archive payload accepted by the ingestion boundary.
/// </summary>
public sealed record ArchiveEnvelope
{
    public ArchiveEnvelope(
        Guid id,
        ArchiveRecordKind kind,
        ArchivePriority priority,
        object record,
        DateTimeOffset enqueuedAtUtc)
    {
        ArchiveContractGuards.NotEmpty(id, nameof(id));
        ArgumentNullException.ThrowIfNull(record);

        Id = id;
        Kind = kind;
        Priority = priority;
        Record = record;
        EnqueuedAtUtc = ArchiveContractGuards.Utc(enqueuedAtUtc);
    }

    public Guid Id { get; }

    public ArchiveRecordKind Kind { get; }

    public ArchivePriority Priority { get; }

    public object Record { get; }

    public DateTimeOffset EnqueuedAtUtc { get; }
}

internal static class ArchiveContractGuards
{
    public static void NotEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }

    public static string NotBlank(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value must not be empty.", parameterName);
        }

        return value.Trim();
    }

    public static void NonNegative(long value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must not be negative.");
        }
    }

    public static void Positive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be greater than zero.");
        }
    }

    public static void EnumDefined<T>(T value, string parameterName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Enum value is unsupported.");
        }
    }

    public static DateTimeOffset Utc(DateTimeOffset value)
        => value.ToUniversalTime();

    public static DateTimeOffset? Utc(DateTimeOffset? value)
        => value?.ToUniversalTime();

    public static IReadOnlyList<T> ReadOnlyCopy<T>(IEnumerable<T>? values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        return Array.AsReadOnly(values.ToArray());
    }
}
