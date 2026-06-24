namespace Configurator.Application.Services.Authorization;

public interface IUserManagementService
{
    Task<bool> IsBootstrapRequiredAsync(CancellationToken cancellationToken = default);

    Task<UserManagementResult> BootstrapAdministratorAsync(
        BootstrapAdministratorRequest request,
        CancellationToken cancellationToken = default);

    Task<UserManagementResult> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default);

    Task<UserManagementResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default);

    Task<UserManagementResult> SetUserEnabledAsync(
        SetUserEnabledRequest request,
        CancellationToken cancellationToken = default);
}
