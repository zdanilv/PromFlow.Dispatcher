namespace Configurator.Application.Services.Authorization;

public interface ICredentialProtector
{
    string Protect(string plaintext);

    bool TryUnprotect(string protectedValue, out string plaintext);
}
