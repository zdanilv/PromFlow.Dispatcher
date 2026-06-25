namespace Configurator.Application.Services.Runtime;

public interface IPersistenceInitializer
{
    Task<PersistenceInitializationResult> InitializeAsync(CancellationToken cancellationToken = default);
}
