using Configurator.Application.Services.Authorization;
using Configurator.Infrastructure.Security;
using Xunit;

namespace Configurator.Tests.Unit.Authorization;

public sealed class LoginCredentialStoreTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "promflow-login-store-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndLoad_RoundTripsEncryptedPassword()
    {
        var path = Path.Combine(_tempDirectory, "login-preferences.json");
        var store = new FileLoginCredentialStore(
            new LoginCredentialPathProvider(path),
            new ReversingCredentialProtector());

        await store.SaveAsync(new LoginCredentialPreferences("operator", "ValidPass123", true, true));
        var json = await File.ReadAllTextAsync(path);
        var loaded = await store.LoadAsync();

        Assert.DoesNotContain("ValidPass123", json, StringComparison.Ordinal);
        Assert.Equal("operator", loaded.Username);
        Assert.Equal("ValidPass123", loaded.Password);
        Assert.True(loaded.RememberPassword);
        Assert.True(loaded.AutoLogin);
    }

    [Fact]
    public async Task SaveWithoutRememberPassword_ClearsExistingFile()
    {
        var path = Path.Combine(_tempDirectory, "login-preferences.json");
        var store = new FileLoginCredentialStore(
            new LoginCredentialPathProvider(path),
            new ReversingCredentialProtector());

        await store.SaveAsync(new LoginCredentialPreferences("operator", "ValidPass123", true, true));
        await store.SaveAsync(new LoginCredentialPreferences("operator", null, false, false));

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task CorruptStore_ReturnsEmptyPreferences()
    {
        var path = Path.Combine(_tempDirectory, "login-preferences.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{not-json");
        var store = new FileLoginCredentialStore(
            new LoginCredentialPathProvider(path),
            new ReversingCredentialProtector());

        var loaded = await store.LoadAsync();

        Assert.Equal(LoginCredentialPreferences.Empty, loaded);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private sealed class ReversingCredentialProtector : ICredentialProtector
    {
        public string Protect(string plaintext) =>
            "protected:" + new string(plaintext.Reverse().ToArray());

        public bool TryUnprotect(string protectedValue, out string plaintext)
        {
            plaintext = string.Empty;
            const string prefix = "protected:";
            if (!protectedValue.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            plaintext = new string(protectedValue[prefix.Length..].Reverse().ToArray());
            return true;
        }
    }
}
