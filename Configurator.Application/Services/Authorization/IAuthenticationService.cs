namespace Configurator.Application.Services.Authorization;

public interface IAuthenticationService
{
    Task<AuthenticationResult> AuthenticateAsync(
        AuthenticationRequest request,
        CancellationToken cancellationToken = default);

    Task SignOutAsync(CancellationToken cancellationToken = default);
}
