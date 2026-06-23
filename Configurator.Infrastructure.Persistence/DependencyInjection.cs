using Configurator.Application.Services.Archiving;
using Configurator.Infrastructure.Persistence.Archive;
using Configurator.Infrastructure.Persistence.Common;
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
        services.AddSingleton<ArchiveOptionsValidator>();
        services.AddSingleton<IAppDataPathProvider, DefaultAppDataPathProvider>();
        services.AddSingleton<ArchiveSnapshotBlobCodec>();
        services.AddSingleton<ArchivePartitionResolver>();
        services.AddSingleton<ArchivePriorityBuffer>();
        services.AddSingleton<ArchiveBackoffPolicy>();
        services.AddSingleton<ArchiveHealthService>();
        services.AddSingleton<IArchiveHealthService>(serviceProvider => serviceProvider.GetRequiredService<ArchiveHealthService>());
        services.AddSingleton<ArchiveIngestor>();
        services.AddSingleton<IArchiveIngestor>(serviceProvider => serviceProvider.GetRequiredService<ArchiveIngestor>());
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<SqlitePragmaInitializer>();
        services.AddSingleton<SqliteMigrationCatalog>();
        services.AddSingleton<SqliteMigrationRunner>();
        services.AddSingleton<SqliteArchiveWriter>();
        services.AddSingleton<ArchiveRuntime>();
        services.AddSingleton<IArchiveRuntime>(serviceProvider => serviceProvider.GetRequiredService<ArchiveRuntime>());

        return services;
    }
}
