using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Authorization;
using Configurator.Infrastructure.Persistence.Security;
using Configurator.Infrastructure.Persistence.Sqlite;
using Microsoft.Extensions.Options;

namespace Configurator.Infrastructure.Persistence.Tests.Security;

internal sealed class SecurityTestFixture : IDisposable
{
    public SecurityTestFixture()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "PromFlow.SecurityTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        DatabasePath = Path.Combine(DirectoryPath, "security.sqlite");
        Options = Microsoft.Extensions.Options.Options.Create(new AuthenticationOptions
        {
            SecurityDatabasePath = DatabasePath,
            MaxFailedAttempts = 2,
            LockoutMinutes = 15,
            MinimumPasswordLength = 8,
            RequireDigit = true,
            RequireUppercase = true,
            RequireLowercase = true
        });
    }

    public string DirectoryPath { get; }

    public string DatabasePath { get; }

    public IOptions<AuthenticationOptions> Options { get; }

    public SecuritySqliteConnectionFactory ConnectionFactory
        => new(new SecurityDatabasePathProvider(Options));

    public SqlitePragmaInitializer PragmaInitializer { get; } = new();

    public SecuritySqliteMigrationCatalog MigrationCatalog { get; } = new();

    public SecuritySqliteMigrationRunner MigrationRunner
        => new(ConnectionFactory, PragmaInitializer, MigrationCatalog, Options);

    public SqliteUserRepository UserRepository
        => new(MigrationRunner, ConnectionFactory, PragmaInitializer, Options);

    public PasswordHashService PasswordHashService { get; } = new();

    public AuthenticationOptionsValidator OptionsValidator { get; } = new();

    public InMemoryUserSessionAccessor SessionAccessor { get; } = new();

    public AuthorizationService CreateAuthorizationService()
        => new(SessionAccessor);

    public RecordingSecurityAuditService AuditService { get; } = new();

    public AuthenticationService CreateAuthenticationService()
    {
        var authorization = CreateAuthorizationService();
        return new AuthenticationService(
            UserRepository,
            PasswordHashService,
            SessionAccessor,
            authorization,
            AuditService,
            OptionsValidator,
            Options);
    }

    public UserManagementService CreateUserManagementService()
    {
        var authorization = CreateAuthorizationService();
        return new UserManagementService(
            UserRepository,
            PasswordHashService,
            authorization,
            SessionAccessor,
            AuditService,
            OptionsValidator,
            Options);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal sealed class RecordingSecurityAuditService : ISecurityAuditService
{
    private readonly List<SecurityAuditRecord> _records = [];

    public IReadOnlyList<SecurityAuditRecord> Records => _records;

    public bool Fail { get; set; }

    public Task<ArchiveOperationResult> RecordAsync(
        SecurityAuditRecord record,
        CancellationToken cancellationToken = default)
    {
        if (Fail)
        {
            return Task.FromResult(ArchiveOperationResult.Failure("AuditUnavailable", "Audit unavailable."));
        }

        _records.Add(record);
        return Task.FromResult(ArchiveOperationResult.Success());
    }
}
