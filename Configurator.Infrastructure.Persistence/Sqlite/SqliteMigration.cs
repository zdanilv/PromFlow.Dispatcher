using System.Security.Cryptography;
using System.Text;

namespace Configurator.Infrastructure.Persistence.Sqlite;

public sealed record SqliteMigration(int Version, string Name, string Sql, string Checksum)
{
    public static SqliteMigration Create(int version, string name, string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        return new SqliteMigration(version, name, sql, ComputeChecksum(sql));
    }

    public static string ComputeChecksum(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sql));

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
