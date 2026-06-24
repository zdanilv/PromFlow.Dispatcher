using Configurator.Application.Services.Licensing;
using System.Text.Json;

namespace Configurator.Infrastructure.Licensing;

public sealed class FileTrustedTimeStateStore : ITrustedTimeStateStore, IDisposable
{
    private readonly LicensePathProvider _pathProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileTrustedTimeStateStore(LicensePathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public async Task<TrustedTimeState?> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = _pathProvider.GetTrustedTimeStatePath();
            if (!File.Exists(path))
            {
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);

            return JsonSerializer.Deserialize<TrustedTimeState>(bytes, LicenseJson.Options);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(TrustedTimeState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = _pathProvider.GetTrustedTimeStatePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllTextAsync(
                tempPath,
                JsonSerializer.Serialize(state, LicenseJson.IndentedOptions),
                cancellationToken).ConfigureAwait(false);

            if (File.Exists(path))
            {
                File.Replace(tempPath, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
