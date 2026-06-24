namespace Configurator.Application.Services.Authorization;

public sealed class AuthenticationOptionsValidator
{
    public AuthenticationOptionsValidationResult Validate(AuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<AuthenticationValidationError>();

        if (options.MaxFailedAttempts <= 0 || options.MaxFailedAttempts > 100)
        {
            errors.Add(new AuthenticationValidationError(
                "AuthenticationLockoutInvalid",
                nameof(AuthenticationOptions.MaxFailedAttempts),
                "Maximum failed attempts must be between 1 and 100."));
        }

        if (options.LockoutMinutes <= 0 || options.LockoutMinutes > 24 * 60)
        {
            errors.Add(new AuthenticationValidationError(
                "AuthenticationLockoutInvalid",
                nameof(AuthenticationOptions.LockoutMinutes),
                "Lockout duration must be between 1 minute and 24 hours."));
        }

        if (options.MinimumPasswordLength < 8 || options.MinimumPasswordLength > 256)
        {
            errors.Add(new AuthenticationValidationError(
                "AuthenticationPasswordPolicyInvalid",
                nameof(AuthenticationOptions.MinimumPasswordLength),
                "Minimum password length must be between 8 and 256."));
        }

        if (!string.IsNullOrWhiteSpace(options.SecurityDatabasePath))
        {
            try
            {
                _ = Path.GetFullPath(options.SecurityDatabasePath);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                errors.Add(new AuthenticationValidationError(
                    "AuthenticationPathInvalid",
                    nameof(AuthenticationOptions.SecurityDatabasePath),
                    "Security database path is invalid."));
            }
        }

        return new AuthenticationOptionsValidationResult(errors.Count == 0, errors);
    }
}

public sealed record AuthenticationValidationError(
    string Code,
    string PropertyName,
    string Message);

public sealed record AuthenticationOptionsValidationResult(
    bool Succeeded,
    IReadOnlyList<AuthenticationValidationError> Errors);
