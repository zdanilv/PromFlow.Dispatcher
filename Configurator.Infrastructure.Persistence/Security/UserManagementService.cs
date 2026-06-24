using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Authorization;
using Microsoft.Extensions.Options;

namespace Configurator.Infrastructure.Persistence.Security;

public sealed class UserManagementService : IUserManagementService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHashService _passwordHashService;
    private readonly IAuthorizationService _authorizationService;
    private readonly IUserSessionAccessor _sessionAccessor;
    private readonly ISecurityAuditService _securityAuditService;
    private readonly AuthenticationOptionsValidator _optionsValidator;
    private readonly IOptions<AuthenticationOptions> _options;

    public UserManagementService(
        IUserRepository userRepository,
        IPasswordHashService passwordHashService,
        IAuthorizationService authorizationService,
        IUserSessionAccessor sessionAccessor,
        ISecurityAuditService securityAuditService,
        AuthenticationOptionsValidator optionsValidator,
        IOptions<AuthenticationOptions> options)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _passwordHashService = passwordHashService ?? throw new ArgumentNullException(nameof(passwordHashService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _sessionAccessor = sessionAccessor ?? throw new ArgumentNullException(nameof(sessionAccessor));
        _securityAuditService = securityAuditService ?? throw new ArgumentNullException(nameof(securityAuditService));
        _optionsValidator = optionsValidator ?? throw new ArgumentNullException(nameof(optionsValidator));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<bool> IsBootstrapRequiredAsync(CancellationToken cancellationToken = default)
        => await _userRepository.CountAsync(cancellationToken).ConfigureAwait(false) == 0;

    public async Task<UserManagementResult> BootstrapAdministratorAsync(
        BootstrapAdministratorRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = ValidateNewUser(request.Username, request.Password, UserRole.Administrator);
        if (validation is not null)
        {
            return validation;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var user = CreateUser(request.Username, UserRole.Administrator, nowUtc);
        user = user.WithPasswordHash(_passwordHashService.HashPassword(user, request.Password), nowUtc);
        var result = await _userRepository.BootstrapAdministratorAsync(user, cancellationToken).ConfigureAwait(false);
        await AuditAsync(
            "BootstrapAdministrator",
            result.Succeeded ? SecurityAuditResult.Succeeded : SecurityAuditResult.Failed,
            result.Value?.Id,
            result.Value?.Username ?? request.Username,
            result.ErrorCode,
            cancellationToken).ConfigureAwait(false);

        return result.Succeeded && result.Value is not null
            ? UserManagementResult.Success(result.Value)
            : UserManagementResult.Failure(
                result.ErrorCode ?? SecurityErrorCodes.SecurityRepositoryFailed,
                result.ErrorMessage ?? "Administrator bootstrap failed.");
    }

    public async Task<UserManagementResult> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var permission = await RequireManageUsersAsync(cancellationToken).ConfigureAwait(false);
        if (!permission.Succeeded)
        {
            return UserManagementResult.Failure(permission.ReasonCode ?? SecurityErrorCodes.PermissionDenied, "Manage users permission is required.");
        }

        var validation = ValidateNewUser(request.Username, request.Password, request.Role);
        if (validation is not null)
        {
            return validation;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var user = CreateUser(request.Username, request.Role, nowUtc) with { IsEnabled = request.IsEnabled };
        user = user.WithPasswordHash(_passwordHashService.HashPassword(user, request.Password), nowUtc);
        var result = await _userRepository.CreateAsync(user, cancellationToken).ConfigureAwait(false);
        await AuditAsync(
            "UserCreated",
            result.Succeeded ? SecurityAuditResult.Succeeded : SecurityAuditResult.Failed,
            result.Value?.Id,
            result.Value?.Username ?? request.Username,
            result.ErrorCode,
            cancellationToken).ConfigureAwait(false);

        return result.Succeeded && result.Value is not null
            ? UserManagementResult.Success(result.Value)
            : UserManagementResult.Failure(
                result.ErrorCode ?? SecurityErrorCodes.SecurityRepositoryFailed,
                result.ErrorMessage ?? "User create failed.");
    }

    public async Task<UserManagementResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var permission = await RequireManageUsersAsync(cancellationToken).ConfigureAwait(false);
        if (!permission.Succeeded)
        {
            return UserManagementResult.Failure(permission.ReasonCode ?? SecurityErrorCodes.PermissionDenied, "Manage users permission is required.");
        }

        var user = await _userRepository.FindByIdAsync(request.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return UserManagementResult.Failure(SecurityErrorCodes.UserNotFound, "User was not found.");
        }

        var policyError = ValidatePassword(request.NewPassword);
        if (policyError is not null)
        {
            return policyError;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var updated = user.WithPasswordHash(_passwordHashService.HashPassword(user, request.NewPassword), nowUtc) with
        {
            FailedLoginCount = 0,
            LockoutUntilUtc = null
        };
        var result = await _userRepository.UpdateAsync(updated, request.ExpectedRowVersion, cancellationToken)
            .ConfigureAwait(false);
        await AuditAsync(
            "UserPasswordChanged",
            result.Succeeded ? SecurityAuditResult.Succeeded : SecurityAuditResult.Failed,
            user.Id,
            user.Username,
            result.ErrorCode,
            cancellationToken).ConfigureAwait(false);

        return result.Succeeded && result.Value is not null
            ? UserManagementResult.Success(result.Value)
            : UserManagementResult.Failure(
                result.ErrorCode ?? SecurityErrorCodes.SecurityRepositoryFailed,
                result.ErrorMessage ?? "User password update failed.");
    }

    public async Task<UserManagementResult> SetUserEnabledAsync(
        SetUserEnabledRequest request,
        CancellationToken cancellationToken = default)
    {
        var permission = await RequireManageUsersAsync(cancellationToken).ConfigureAwait(false);
        if (!permission.Succeeded)
        {
            return UserManagementResult.Failure(permission.ReasonCode ?? SecurityErrorCodes.PermissionDenied, "Manage users permission is required.");
        }

        var user = await _userRepository.FindByIdAsync(request.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return UserManagementResult.Failure(SecurityErrorCodes.UserNotFound, "User was not found.");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var updated = user with
        {
            IsEnabled = request.IsEnabled,
            FailedLoginCount = request.IsEnabled ? 0 : user.FailedLoginCount,
            LockoutUntilUtc = request.IsEnabled ? null : user.LockoutUntilUtc,
            UpdatedAtUtc = nowUtc
        };
        var result = await _userRepository.UpdateAsync(updated, request.ExpectedRowVersion, cancellationToken)
            .ConfigureAwait(false);
        await AuditAsync(
            request.IsEnabled ? "UserEnabled" : "UserDisabled",
            result.Succeeded ? SecurityAuditResult.Succeeded : SecurityAuditResult.Failed,
            user.Id,
            user.Username,
            result.ErrorCode,
            cancellationToken).ConfigureAwait(false);

        return result.Succeeded && result.Value is not null
            ? UserManagementResult.Success(result.Value)
            : UserManagementResult.Failure(
                result.ErrorCode ?? SecurityErrorCodes.SecurityRepositoryFailed,
                result.ErrorMessage ?? "User enabled state update failed.");
    }

    private async Task<AuthorizationDecision> RequireManageUsersAsync(CancellationToken cancellationToken)
        => await _authorizationService.AuthorizeAsync(Permission.ManageUsers, cancellationToken).ConfigureAwait(false);

    private UserManagementResult? ValidateNewUser(string username, string password, UserRole role)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return UserManagementResult.Failure("UsernameRequired", "Username is required.");
        }

        if (!Enum.IsDefined(role))
        {
            return UserManagementResult.Failure("UserRoleUnsupported", "User role is unsupported.");
        }

        return ValidatePassword(password);
    }

    private UserManagementResult? ValidatePassword(string password)
    {
        var options = _options.Value.Clone();
        var optionsValidation = _optionsValidator.Validate(options);
        if (!optionsValidation.Succeeded)
        {
            return UserManagementResult.Failure(SecurityErrorCodes.SecurityOptionsInvalid, "Authentication options are invalid.");
        }

        var policy = options.ToPasswordPolicy();
        if (string.IsNullOrEmpty(password)
            || password.Length < policy.MinimumLength
            || (policy.RequireDigit && !password.Any(char.IsDigit))
            || (policy.RequireUppercase && !password.Any(char.IsUpper))
            || (policy.RequireLowercase && !password.Any(char.IsLower)))
        {
            return UserManagementResult.Failure(
                SecurityErrorCodes.PasswordPolicyViolation,
                "Password does not satisfy the configured policy.");
        }

        return null;
    }

    private static AppUser CreateUser(string username, UserRole role, DateTimeOffset nowUtc)
        => new(
            Guid.NewGuid(),
            username,
            AppUser.NormalizeUsername(username),
            "pending-password-hash",
            role,
            isEnabled: true,
            failedLoginCount: 0,
            lockoutUntilUtc: null,
            nowUtc,
            nowUtc,
            nowUtc,
            lastLoginAtUtc: null,
            rowVersion: 0);

    private async Task AuditAsync(
        string eventType,
        SecurityAuditResult result,
        Guid? targetUserId,
        string? targetUsername,
        string? reasonCode,
        CancellationToken cancellationToken)
    {
        var session = _sessionAccessor.Current.Session;
        var record = new SecurityAuditRecord(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            eventType,
            result == SecurityAuditResult.Succeeded ? SecurityAuditSeverity.Information : SecurityAuditSeverity.Warning,
            session?.UserId.ToString("D"),
            session?.Username,
            session?.SessionId.ToString("D"),
            targetUserId?.ToString("D"),
            result,
            reasonCode,
            detailsJson: targetUsername is null ? null : "{\"target\":\"user\"}",
            schemaVersion: 1);

        try
        {
            await _securityAuditService.RecordAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
        }
    }
}
