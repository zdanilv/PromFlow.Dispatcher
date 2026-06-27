namespace Configurator.Application.Services.Authorization;

public sealed class NullLoginCredentialStore : ILoginCredentialStore
{
    public Task<LoginCredentialPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(LoginCredentialPreferences.Empty);
    }

    public Task SaveAsync(LoginCredentialPreferences preferences, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
