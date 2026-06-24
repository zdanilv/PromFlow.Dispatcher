namespace Configurator.Infrastructure.Persistence.Security;

public static class SecurityErrorCodes
{
    public const string SecurityDatabasePathInvalid = nameof(SecurityDatabasePathInvalid);
    public const string SecurityDatabaseDirectoryInvalid = nameof(SecurityDatabaseDirectoryInvalid);
    public const string SecurityDatabaseConnectionFailed = nameof(SecurityDatabaseConnectionFailed);
    public const string SecurityMigrationFailed = nameof(SecurityMigrationFailed);
    public const string SecurityMigrationChecksumMismatch = nameof(SecurityMigrationChecksumMismatch);
    public const string SecurityOptionsInvalid = nameof(SecurityOptionsInvalid);
    public const string UserDuplicate = nameof(UserDuplicate);
    public const string UserNotFound = nameof(UserNotFound);
    public const string UserRowVersionConflict = nameof(UserRowVersionConflict);
    public const string BootstrapAlreadyCompleted = nameof(BootstrapAlreadyCompleted);
    public const string PasswordPolicyViolation = nameof(PasswordPolicyViolation);
    public const string PermissionDenied = nameof(PermissionDenied);
    public const string SecurityRepositoryFailed = nameof(SecurityRepositoryFailed);
}
