namespace Configurator.Application.Services.Authorization;

public sealed record LoginCredentialPreferences(
    string Username,
    string? Password,
    bool RememberPassword,
    bool AutoLogin)
{
    public static LoginCredentialPreferences Empty { get; } = new(string.Empty, null, false, false);
}
