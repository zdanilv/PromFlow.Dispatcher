using Configurator.Application.Services.Archiving;
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
        services.AddSingleton<IAppDataPathProvider, DefaultAppDataPathProvider>();
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<SqlitePragmaInitializer>();
        services.AddSingleton<SqliteMigrationCatalog>();
        services.AddSingleton<SqliteMigrationRunner>();

        return services;
    }
}
