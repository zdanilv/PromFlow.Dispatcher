using System.Text.Json;
using Configurator.Application.Services.Authorization;

namespace Configurator.Infrastructure.Security;

public sealed class FileLoginCredentialStore(
    LoginCredentialPathProvider pathProvider,
    ICredentialProtector credentialProtector) : ILoginCredentialStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<LoginCredentialPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        var path = pathProvider.GetPreferencesFilePath();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                return LoginCredentialPreferences.Empty;
            }

            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var document = JsonSerializer.Deserialize<LoginCredentialDocument>(json, JsonOptions);
            if (document is null || !document.RememberPassword)
            {
                return LoginCredentialPreferences.Empty;
            }

            if (string.IsNullOrWhiteSpace(document.ProtectedPassword)
                || !credentialProtector.TryUnprotect(document.ProtectedPassword, out var password))
            {
                return LoginCredentialPreferences.Empty;
            }

            return new LoginCredentialPreferences(
                document.Username ?? string.Empty,
                password,
                RememberPassword: true,
                AutoLogin: document.AutoLogin);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return LoginCredentialPreferences.Empty;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(LoginCredentialPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        if (!preferences.RememberPassword)
        {
            await ClearAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(preferences.Password))
        {
            await ClearAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var path = pathProvider.GetPreferencesFilePath();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var document = new LoginCredentialDocument
            {
                Username = preferences.Username,
                ProtectedPassword = credentialProtector.Protect(preferences.Password),
                RememberPassword = true,
                AutoLogin = preferences.AutoLogin
            };
            var json = JsonSerializer.Serialize(document, JsonOptions);
            await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var path = pathProvider.GetPreferencesFilePath();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class LoginCredentialDocument
    {
        public string? Username { get; set; }

        public string? ProtectedPassword { get; set; }

        public bool RememberPassword { get; set; }

        public bool AutoLogin { get; set; }
    }
}
