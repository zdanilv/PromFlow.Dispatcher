using Microsoft.Data.Sqlite;

namespace Configurator.Infrastructure.Persistence.Sqlite;

public static class SqliteDataReaderExtensions
{
    public static Guid GetGuidFromString(this SqliteDataReader reader, int ordinal)
        => Guid.Parse(reader.GetString(ordinal));

    public static DateTimeOffset GetUtcFromUnixMilliseconds(this SqliteDataReader reader, int ordinal)
        => DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(ordinal));

    public static DateTimeOffset? GetNullableUtcFromUnixMilliseconds(this SqliteDataReader reader, int ordinal)
    {
        var value = reader.GetNullableInt64(ordinal);
        return value is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(value.Value);
    }

    public static string? GetNullableString(this SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static long? GetNullableInt64(this SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    public static int? GetNullableInt32(this SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
}
