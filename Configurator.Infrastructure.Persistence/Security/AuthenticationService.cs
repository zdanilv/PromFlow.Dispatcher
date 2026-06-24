using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Authorization;
using Microsoft.Extensions.Options;

namespace Configurator.Infrastructure.Persistence.Security;

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHashService _passwordHashService;
    private readonly IUserSessionAccessor _sessionAccessor;
    private readonly IAuthorizationService _authorizationService;
    private readonly ISecurityAuditService _securityAuditService;
    private readonly AuthenticationOptionsValidator _optionsValidator;
    private readonly IOptions<AuthenticationOptions> _options;

    public AuthenticationService(
        IUserRepository userRepository,
        IPasswordHashService passwordHashService,
        IUserSessionAccessor sessionAccessor,
        IAuthorizationService authorizationService,
        ISecurityAuditService securityAuditService,
        AuthenticationOptionsValidator optionsValidator,
        IOptions<AuthenticationOptions> options)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _passwordHashService = passwordHashService ?? throw new ArgumentNullException(nameof(passwordHashService));
        _sessionAccessor = sessionAccessor ?? throw new ArgumentNullException(nameof(sessionAccessor));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _securityAuditService = securityAuditService ?? throw new ArgumentNullException(nameof(securityAuditService));
        _optionsValidator = optionsValidator ?? throw new ArgumentNullException(nameof(optionsValidator));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<AuthenticationResult> AuthenticateAsync(
        AuthenticationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var options = _options.Value.Clone();
        var validation = _optionsValidator.Validate(options);
        if (!validation.Succeeded)
        {
            return AuthenticationResult.Failure(
                SecurityErrorCodes.SecurityOptionsInvalid,
                "Authentication options are invalid.");
        }

        try
        {
            if (await _userRepository.CountAsync(cancellationToken).ConfigureAwait(false) == 0)
            {
                return AuthenticationResult.BootstrapRequired();
            }

            var normalizedUsername = AppUser.NormalizeUsername(request.Username);
            if (string.IsNullOrWhiteSpace(normalizedUsername))
            {
                await AuditAsync(
                    "AuthenticationFailed",
                    SecurityAuditSeverity.Warning,
                    null,
                    request.Username,
                    null,
                    SecurityAuditResult.Denied,
                    "InvalidCredentials",
                    cancellationToken).ConfigureAwait(false);

                return AuthenticationResult.InvalidCredentials();
            }

            var user = await _userRepository
                .FindByNormalizedUsernameAsync(normalizedUsername, cancellationToken)
                .ConfigureAwait(false);
            if (user is null)
            {
                await AuditAsync(
                    "AuthenticationFailed",
                    SecurityAuditSeverity.Warning,
                    null,
                    normalizedUsername,
                    null,
                    SecurityAuditResult.Denied,
                    "InvalidCredentials",
                    cancellationToken).ConfigureAwait(false);

                return AuthenticationResult.InvalidCredentials();
            }

            var nowUtc = DateTimeOffset.UtcNow;
            if (!user.IsEnabled)
            {
                await AuditAsync(
                    "AuthenticationFailed",
                    SecurityAuditSeverity.Warning,
                    user.Id,
                    user.Username,
                    null,
                    SecurityAuditResult.Denied,
                    "UserDisabled",
                    cancellationToken).ConfigureAwait(false);

                return AuthenticationResult.InvalidCredentials();
            }

            if (user.LockoutUntilUtc is { } lockoutUntilUtc && lockoutUntilUtc > nowUtc)
            {
                await AuditAsync(
                    "AuthenticationLockedOut",
                    SecurityAuditSeverity.Warning,
                    user.Id,
                    user.Username,
                    null,
                    SecurityAuditResult.Denied,
                    "UserLockedOut",
                    cancellationToken).ConfigureAwait(false);

                return AuthenticationResult.LockedOut(lockoutUntilUtc);
            }

            var verification = _passwordHashService.VerifyPassword(user, request.Password);
            if (verification == PasswordHashVerificationResult.Failed)
            {
                return await RecordFailedLoginAsync(user, options.ToLockoutPolicy(), nowUtc, cancellationToken)
                    .ConfigureAwait(false);
            }

            var passwordHash = verification == PasswordHashVerificationResult.SuccessRehashNeeded
                ? _passwordHashService.HashPassword(user, request.Password)
                : user.PasswordHash;
            var updated = user with
            {
                PasswordHash = passwordHash,
                FailedLoginCount = 0,
                LockoutUntilUtc = null,
                LastLoginAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PasswordChangedAtUtc = verification == PasswordHashVerificationResult.SuccessRehashNeeded
                    ? nowUtc
                    : user.PasswordChangedAtUtc
            };
            var updateResult = await _userRepository.UpdateAsync(updated, user.RowVersion, cancellationToken)
                .ConfigureAwait(false);
            if (!updateResult.Succeeded || updateResult.Value is null)
            {
                return AuthenticationResult.Failure(
                    updateResult.ErrorCode ?? SecurityErrorCodes.SecurityRepositoryFailed,
                    updateResult.ErrorMessage ?? "Authentication state update failed.");
            }

            var session = new UserSession(
                Guid.NewGuid(),
                updateResult.Value.Id,
                updateResult.Value.Username,
                updateResult.Value.Role,
                _authorizationService.GetPermissions(updateResult.Value.Role),
                nowUtc);
            _sessionAccessor.SetCurrent(session);
            await AuditAsync(
                "AuthenticationSucceeded",
                SecurityAuditSeverity.Information,
                updateResult.Value.Id,
                updateResult.Value.Username,
                session.SessionId,
                SecurityAuditResult.Succeeded,
                null,
                cancellationToken).ConfigureAwait(false);

            return AuthenticationResult.Success(session);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return AuthenticationResult.Failure(
                SecurityErrorCodes.SecurityRepositoryFailed,
                "Authentication failed.");
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var session = _sessionAccessor.Current.Session;
        _sessionAccessor.Clear();
        if (session is not null)
        {
            await AuditAsync(
                "SignOut",
                SecurityAuditSeverity.Information,
                session.UserId,
                session.Username,
                session.SessionId,
                SecurityAuditResult.Succeeded,
                null,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<AuthenticationResult> RecordFailedLoginAsync(
        AppUser user,
        UserLockoutPolicy lockoutPolicy,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var failedCount = user.FailedLoginCount + 1;
        var lockoutUntilUtc = failedCount >= lockoutPolicy.MaxFailedAttempts
            ? nowUtc.Add(lockoutPolicy.Duration)
            : (DateTimeOffset?)null;
        var updated = user with
        {
            FailedLoginCount = failedCount,
            LockoutUntilUtc = lockoutUntilUtc,
            UpdatedAtUtc = nowUtc
        };
        await _userRepository.UpdateAsync(updated, user.RowVersion, cancellationToken).ConfigureAwait(false);
        await AuditAsync(
            lockoutUntilUtc is null ? "AuthenticationFailed" : "AuthenticationLockedOut",
            SecurityAuditSeverity.Warning,
            user.Id,
            user.Username,
            null,
            SecurityAuditResult.Denied,
            lockoutUntilUtc is null ? "InvalidCredentials" : "UserLockedOut",
            cancellationToken).ConfigureAwait(false);

        return lockoutUntilUtc is null
            ? AuthenticationResult.InvalidCredentials()
            : AuthenticationResult.LockedOut(lockoutUntilUtc.Value);
    }

    private async Task AuditAsync(
        string eventType,
        SecurityAuditSeverity severity,
        Guid? actorUserId,
        string? actorUsername,
        Guid? sessionId,
        SecurityAuditResult result,
        string? reasonCode,
        CancellationToken cancellationToken)
    {
        var record = new SecurityAuditRecord(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            eventType,
            severity,
            actorUserId?.ToString("D"),
            actorUsername,
            sessionId?.ToString("D"),
            targetUserId: null,
            result,
            reasonCode,
            detailsJson: null,
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
