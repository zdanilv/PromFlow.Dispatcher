using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Authorization;
using Configurator.Infrastructure.Persistence.Archive;
using Configurator.Infrastructure.Persistence.Common;
using Configurator.Infrastructure.Persistence.Security;
using Configurator.Infrastructure.Persistence.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Configurator.Infrastructure.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistenceInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ArchiveOptions>(configuration.GetSection(ArchiveOptions.SectionName));
        services.Configure<AuthenticationOptions>(configuration.GetSection(AuthenticationOptions.SectionName));
        services.AddSingleton<ArchiveOptionsValidator>();
        services.AddSingleton<AuthenticationOptionsValidator>();
        services.AddSingleton<IAppDataPathProvider, DefaultAppDataPathProvider>();
        services.AddSingleton<ArchiveSnapshotBlobCodec>();
        services.AddSingleton<ArchivePartitionResolver>();
        services.AddSingleton<ArchivePriorityBuffer>();
        services.AddSingleton<ArchiveBackoffPolicy>();
        services.AddSingleton<ArchiveHealthService>();
        services.AddSingleton<IArchiveHealthService>(serviceProvider => serviceProvider.GetRequiredService<ArchiveHealthService>());
        services.AddSingleton<ArchiveIngestor>();
        services.AddSingleton<IArchiveIngestor>(serviceProvider => serviceProvider.GetRequiredService<ArchiveIngestor>());
        services.AddSingleton<CommandAuditService>();
        services.AddSingleton<ICommandAuditService>(serviceProvider => serviceProvider.GetRequiredService<CommandAuditService>());
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<SqlitePragmaInitializer>();
        services.AddSingleton<SqliteMigrationCatalog>();
        services.AddSingleton<SqliteMigrationRunner>();
        services.AddSingleton<SecurityDatabasePathProvider>();
        services.AddSingleton<SecuritySqliteConnectionFactory>();
        services.AddSingleton<SecuritySqliteMigrationCatalog>();
        services.AddSingleton<SecuritySqliteMigrationRunner>();
        services.AddSingleton<PasswordHashService>();
        services.AddSingleton<IPasswordHashService>(serviceProvider => serviceProvider.GetRequiredService<PasswordHashService>());
        services.AddSingleton<InMemoryUserSessionAccessor>();
        services.AddSingleton<IUserSessionAccessor>(serviceProvider => serviceProvider.GetRequiredService<InMemoryUserSessionAccessor>());
        services.AddSingleton<AuthorizationService>();
        services.AddSingleton<IAuthorizationService>(serviceProvider => serviceProvider.GetRequiredService<AuthorizationService>());
        services.AddSingleton<SqliteUserRepository>();
        services.AddSingleton<IUserRepository>(serviceProvider => serviceProvider.GetRequiredService<SqliteUserRepository>());
        services.AddSingleton<SecurityAuditService>();
        services.AddSingleton<ISecurityAuditService>(serviceProvider => serviceProvider.GetRequiredService<SecurityAuditService>());
        services.AddSingleton<AuthenticationService>();
        services.AddSingleton<IAuthenticationService>(serviceProvider => serviceProvider.GetRequiredService<AuthenticationService>());
        services.AddSingleton<UserManagementService>();
        services.AddSingleton<IUserManagementService>(serviceProvider => serviceProvider.GetRequiredService<UserManagementService>());
        services.AddSingleton<ArchivePartitionCatalog>();
        services.AddSingleton<SqliteArchiveQueryService>();
        services.AddSingleton<IArchiveQueryService>(serviceProvider => serviceProvider.GetRequiredService<SqliteArchiveQueryService>());
        services.AddSingleton<ArchiveRuntimeEventWriter>();
        services.AddSingleton<ArchiveChecksum>();
        services.AddSingleton<ArchiveCsvWriter>();
        services.AddSingleton<ArchiveExportPackageWriter>();
        services.AddSingleton<ArchiveBackupPackageWriter>();
        services.AddSingleton<ArchiveMaintenanceService>();
        services.AddSingleton<IArchiveMaintenanceService>(serviceProvider => serviceProvider.GetRequiredService<ArchiveMaintenanceService>());
        services.AddSingleton<SqliteArchiveWriter>();
        services.AddSingleton<ArchiveRuntime>();
        services.AddSingleton<IArchiveRuntime>(serviceProvider => serviceProvider.GetRequiredService<ArchiveRuntime>());

        return services;
    }
}
