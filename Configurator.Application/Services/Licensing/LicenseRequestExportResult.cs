namespace Configurator.Application.Services.Licensing;

public sealed record LicenseRequestExportResult(
    bool Succeeded,
    string? Path,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static LicenseRequestExportResult Success(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return new LicenseRequestExportResult(true, path, null, null);
    }

    public static LicenseRequestExportResult Failure(string errorCode, string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        return new LicenseRequestExportResult(false, null, errorCode, errorMessage);
    }
}
