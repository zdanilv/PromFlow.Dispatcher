namespace Configurator.Application.Services.Authorization;

public sealed record AuthenticationRequest
{
    public AuthenticationRequest(string username, string password)
    {
        Username = username?.Trim() ?? string.Empty;
        Password = password ?? string.Empty;
    }

    public string Username { get; }

    public string Password { get; }
}
