using Configurator.Application.Services.Licensing;
using System.Security.Cryptography;
using System.Text.Json;

namespace Configurator.Infrastructure.Licensing;

public sealed class FileInstallationIdentityService : IInstallationIdentityService, IDisposable
{
    private const int InstallationIdBytes = 32;

    private readonly LicensePathProvider _pathProvider;
    private readonly LicensingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileInstallationIdentityService(
        LicensePathProvider pathProvider,
        LicensingOptions options,
        TimeProvider timeProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _options = options?.Clone() ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<InstallationIdentity> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = _pathProvider.GetInstallationIdentityPath();
            var existing = await ReadExistingAsync(path, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return existing;
            }

            var identity = new InstallationIdentity(
                Base64UrlCodec.Encode(RandomNumberGenerator.GetBytes(InstallationIdBytes)),
                _timeProvider.GetUtcNow().ToUniversalTime());
            await WriteAtomicAsync(path, identity, cancellationToken).ConfigureAwait(false);

            return identity;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<InstallationIdentityRequest> CreateRequestAsync(CancellationToken cancellationToken = default)
    {
        var identity = await GetOrCreateAsync(cancellationToken).ConfigureAwait(false);

        return new InstallationIdentityRequest(
            LicenseConstants.RequestFormat,
            _options.Product,
            identity.InstallationId,
            identity.CreatedAtUtc,
            _timeProvider.GetUtcNow().ToUniversalTime());
    }

    public void Dispose() => _gate.Dispose();

    private static async Task<InstallationIdentity?> ReadExistingAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var identity = JsonSerializer.Deserialize<InstallationIdentity>(bytes, LicenseJson.Options);
            return IsValid(identity) ? identity : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static bool IsValid(InstallationIdentity? identity)
        => identity is not null
            && !string.IsNullOrWhiteSpace(identity.InstallationId)
            && Base64UrlCodec.TryDecode(identity.InstallationId, out var bytes)
            && bytes.Length == InstallationIdBytes;

    private static async Task WriteAtomicAsync(
        string path,
        InstallationIdentity identity,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(
            tempPath,
            JsonSerializer.Serialize(identity, LicenseJson.IndentedOptions),
            cancellationToken).ConfigureAwait(false);

        ReplaceFile(tempPath, path);
    }

    private static void ReplaceFile(string tempPath, string path)
    {
        if (File.Exists(path))
        {
            File.Replace(tempPath, path, destinationBackupFileName: null);
            return;
        }

        File.Move(tempPath, path);
    }
}
