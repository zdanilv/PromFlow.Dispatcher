namespace Configurator.Application.Services.Licensing;

public sealed record LicenseStoreWriteResult(
    bool Succeeded,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static LicenseStoreWriteResult Success()
        => new(true, null, null);

    public static LicenseStoreWriteResult Failure(string errorCode, string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        return new LicenseStoreWriteResult(false, errorCode, errorMessage);
    }
}
