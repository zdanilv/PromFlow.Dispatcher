namespace Configurator.Application.Services.Licensing;

public sealed class LicenseStoreReadResult
{
    private LicenseStoreReadResult(bool succeeded, bool isMissing, byte[] bytes, string? errorMessage)
    {
        Succeeded = succeeded;
        IsMissing = isMissing;
        Bytes = bytes;
        ErrorMessage = errorMessage;
    }

    public bool Succeeded { get; }

    public bool IsMissing { get; }

    public byte[] Bytes { get; }

    public string? ErrorMessage { get; }

    public static LicenseStoreReadResult Found(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        return new LicenseStoreReadResult(succeeded: true, isMissing: false, bytes, errorMessage: null);
    }

    public static LicenseStoreReadResult Missing()
        => new(succeeded: true, isMissing: true, [], errorMessage: null);

    public static LicenseStoreReadResult Failure(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        return new LicenseStoreReadResult(succeeded: false, isMissing: false, [], errorMessage);
    }
}
