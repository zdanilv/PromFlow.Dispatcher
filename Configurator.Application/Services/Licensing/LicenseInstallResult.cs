namespace Configurator.Application.Services.Licensing;

public sealed record LicenseInstallResult(
    bool Succeeded,
    LicenseState State,
    LicenseValidationErrorCode? ReasonCode,
    string? PreviousLicenseId,
    string? NewLicenseId,
    string Message)
{
    public static LicenseInstallResult Success(
        LicenseState state,
        string? previousLicenseId,
        string? newLicenseId)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new LicenseInstallResult(
            true,
            state,
            null,
            previousLicenseId,
            newLicenseId,
            "License installed.");
    }

    public static LicenseInstallResult Failure(
        LicenseState state,
        LicenseValidationErrorCode? reasonCode,
        string? previousLicenseId,
        string? newLicenseId,
        string message)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new LicenseInstallResult(
            false,
            state,
            reasonCode,
            previousLicenseId,
            newLicenseId,
            message);
    }
}
