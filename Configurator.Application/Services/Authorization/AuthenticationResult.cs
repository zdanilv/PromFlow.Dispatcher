namespace Configurator.Application.Services.Authorization;

public enum AuthenticationResultStatus
{
    Succeeded,
    InvalidCredentials,
    LockedOut,
    BootstrapRequired,
    Failed
}

public sealed record AuthenticationResult
{
    private AuthenticationResult(
        AuthenticationResultStatus status,
        UserSession? session,
        DateTimeOffset? lockoutUntilUtc,
        string? errorCode,
        string? errorMessage)
    {
        Status = status;
        Session = session;
        LockoutUntilUtc = lockoutUntilUtc?.ToUniversalTime();
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public AuthenticationResultStatus Status { get; }

    public bool Succeeded => Status == AuthenticationResultStatus.Succeeded;

    public UserSession? Session { get; }

    public DateTimeOffset? LockoutUntilUtc { get; }

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public static AuthenticationResult Success(UserSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new AuthenticationResult(AuthenticationResultStatus.Succeeded, session, null, null, null);
    }

    public static AuthenticationResult InvalidCredentials()
        => new(
            AuthenticationResultStatus.InvalidCredentials,
            null,
            null,
            "InvalidCredentials",
            "Invalid username or password.");

    public static AuthenticationResult LockedOut(DateTimeOffset lockoutUntilUtc)
        => new(
            AuthenticationResultStatus.LockedOut,
            null,
            lockoutUntilUtc,
            "UserLockedOut",
            "Invalid username or password.");

    public static AuthenticationResult BootstrapRequired()
        => new(
            AuthenticationResultStatus.BootstrapRequired,
            null,
            null,
            "BootstrapRequired",
            "Administrator bootstrap is required.");

    public static AuthenticationResult Failure(string errorCode, string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        return new AuthenticationResult(AuthenticationResultStatus.Failed, null, null, errorCode, errorMessage);
    }
}
