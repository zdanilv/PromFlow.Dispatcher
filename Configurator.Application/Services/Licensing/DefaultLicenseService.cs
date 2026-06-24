namespace Configurator.Application.Services.Licensing;

public sealed class DefaultLicenseService : ILicenseService
{
    private readonly ILicenseStore _licenseStore;
    private readonly ILicenseVerifier _licenseVerifier;
    private readonly TimeProvider _timeProvider;

    public DefaultLicenseService(
        ILicenseStore licenseStore,
        ILicenseVerifier licenseVerifier,
        TimeProvider timeProvider)
    {
        _licenseStore = licenseStore ?? throw new ArgumentNullException(nameof(licenseStore));
        _licenseVerifier = licenseVerifier ?? throw new ArgumentNullException(nameof(licenseVerifier));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<LicenseState> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var evaluatedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        var read = await _licenseStore.ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!read.Succeeded)
        {
            return LicenseState.StoreUnavailable(evaluatedAtUtc, read.ErrorMessage ?? "License store is unavailable.");
        }

        if (read.IsMissing)
        {
            return LicenseState.Missing(evaluatedAtUtc);
        }

        var validation = await _licenseVerifier.VerifyAsync(read.Bytes, cancellationToken).ConfigureAwait(false);

        return LicenseState.FromValidation(validation, evaluatedAtUtc);
    }

    public Task<LicenseValidationResult> VerifyAsync(
        byte[] licenseBytes,
        CancellationToken cancellationToken = default)
        => _licenseVerifier.VerifyAsync(licenseBytes, cancellationToken);
}
