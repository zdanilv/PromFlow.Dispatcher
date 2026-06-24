namespace Configurator.Application.Services.Authorization;

public interface IAuthorizationService
{
    Task<AuthorizationDecision> AuthorizeAsync(
        Permission permission,
        CancellationToken cancellationToken = default);

    IReadOnlyList<Permission> GetPermissions(UserRole role);
}
