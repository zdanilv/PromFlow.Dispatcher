using System.Reflection;
using System.Text;
using Configurator.Infrastructure.Persistence.Sqlite;

namespace Configurator.Infrastructure.Persistence.Security;

public sealed class SecuritySqliteMigrationCatalog
{
    private const string UserAccountsMigrationResourceSuffix = "Security.Migrations.001_user_accounts.sql";

    public SecuritySqliteMigrationCatalog()
    {
        All =
        [
            LoadMigration(1, "user_accounts", UserAccountsMigrationResourceSuffix)
        ];
    }

    public IReadOnlyList<SqliteMigration> All { get; }

    private static SqliteMigration LoadMigration(
        int version,
        string name,
        string resourceSuffix)
    {
        var assembly = typeof(SecuritySqliteMigrationCatalog).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .Single(candidate => candidate.EndsWith(resourceSuffix, StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var sql = reader.ReadToEnd();

        return SqliteMigration.Create(version, name, sql);
    }
}
