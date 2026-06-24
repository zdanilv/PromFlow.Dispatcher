using Configurator.Application.Services.Licensing;

namespace Configurator.Infrastructure.Licensing;

public sealed class FileLicenseStore : ILicenseStore, ILicenseWritableStore, IDisposable
{
    private readonly LicensePathProvider _pathProvider;
    private readonly LicensingOptions _options;
    private readonly SemaphoreSlim _fileGate = new(1, 1);
    private bool _disposed;

    public FileLicenseStore(LicensePathProvider pathProvider, LicensingOptions options)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _options = options?.Clone() ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<LicenseStoreReadResult> ReadCurrentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadCurrentCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileGate.Release();
        }
    }

    public async Task<LicenseStoreWriteResult> ReplaceCurrentAsync(
        byte[] licenseBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(licenseBytes);
        var path = _pathProvider.GetCurrentLicensePath();
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await _fileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_pathProvider.GetLicenseDirectory());
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(licenseBytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                File.Replace(tempPath, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, path);
            }

            return LicenseStoreWriteResult.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(tempPath);
            return LicenseStoreWriteResult.Failure("StoreUnavailable", ex.Message);
        }
        finally
        {
            _fileGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _fileGate.Dispose();
    }

    private async Task<LicenseStoreReadResult> ReadCurrentCoreAsync(CancellationToken cancellationToken)
    {
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

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
