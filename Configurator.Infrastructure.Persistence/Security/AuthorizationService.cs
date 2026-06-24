using Configurator.Application.Services.Authorization;

namespace Configurator.Infrastructure.Persistence.Security;

public sealed class AuthorizationService : IAuthorizationService
{
    private static readonly IReadOnlyList<Permission> AdministratorPermissions =
        Array.AsReadOnly(Enum.GetValues<Permission>());

    private static readonly IReadOnlyList<Permission> UserPermissions = Array.AsReadOnly(
    [
        Permission.ViewRouteMap,
        Permission.IssueEquipmentCommands,
        Permission.ViewSignalMapping,
        Permission.ViewModbusDiagnostics,
        Permission.ViewLicense
    ]);

    private readonly IUserSessionAccessor _sessionAccessor;

    public AuthorizationService(IUserSessionAccessor sessionAccessor)
    {
        _sessionAccessor = sessionAccessor ?? throw new ArgumentNullException(nameof(sessionAccessor));
    }

    public Task<AuthorizationDecision> AuthorizeAsync(
        Permission permission,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Enum.IsDefined(permission))
        {
            return Task.FromResult(AuthorizationDecision.Deny(permission, "PermissionUnsupported"));
        }

        var snapshot = _sessionAccessor.Current;
        if (!snapshot.IsAuthenticated || snapshot.Session is null)
        {
            return Task.FromResult(AuthorizationDecision.Deny(permission, "NotAuthenticated"));
        }

        return Task.FromResult(snapshot.Session.HasPermission(permission)
            ? AuthorizationDecision.Allow(permission, snapshot.Session)
            : AuthorizationDecision.Deny(permission, SecurityErrorCodes.PermissionDenied, snapshot.Session));
    }

    public IReadOnlyList<Permission> GetPermissions(UserRole role)
        => role switch
        {
            UserRole.Administrator => AdministratorPermissions,
            UserRole.User => UserPermissions,
            _ => Array.Empty<Permission>()
        };
}
