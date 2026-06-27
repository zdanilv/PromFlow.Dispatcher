namespace Configurator.Application.Services.Authorization;

public interface ILoginCredentialStore
{
    Task<LoginCredentialPreferences> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(LoginCredentialPreferences preferences, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
