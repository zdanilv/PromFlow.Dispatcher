namespace Configurator.Infrastructure.Security;

public sealed class LoginCredentialPathProvider
{
    private readonly string? _preferencesFilePath;

    public LoginCredentialPathProvider()
    {
    }

    public LoginCredentialPathProvider(string preferencesFilePath)
    {
        if (string.IsNullOrWhiteSpace(preferencesFilePath))
        {
            throw new ArgumentException("Preferences file path must not be empty.", nameof(preferencesFilePath));
        }

        _preferencesFilePath = preferencesFilePath;
    }

    public string GetPreferencesFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_preferencesFilePath))
        {
            return _preferencesFilePath;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = AppContext.BaseDirectory;
        }

        return Path.Combine(localAppData, "PromFlow.Dispatcher", "auth", "login-preferences.json");
    }
}
