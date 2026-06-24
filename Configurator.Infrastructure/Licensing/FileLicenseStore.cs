using Configurator.Application.Services.Licensing;

namespace Configurator.Infrastructure.Licensing;

public sealed class FileLicenseStore : ILicenseStore
{
    private readonly LicensePathProvider _pathProvider;
    private readonly LicensingOptions _options;

    public FileLicenseStore(LicensePathProvider pathProvider, LicensingOptions options)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _options = options?.Clone() ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<LicenseStoreReadResult> ReadCurrentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = _pathProvider.GetCurrentLicensePath();
        try
        {
            if (!File.Exists(path))
            {
                return LicenseStoreReadResult.Missing();
            }

            var maxBytes = Math.Max(1, _options.MaxLicenseFileBytes);
            var fileInfo = new FileInfo(path);
            if (fileInfo.Length > maxBytes)
            {
                return LicenseStoreReadResult.Found(new byte[maxBytes + 1]);
            }

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);

            return LicenseStoreReadResult.Found(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return LicenseStoreReadResult.Failure(ex.Message);
        }
    }
}
