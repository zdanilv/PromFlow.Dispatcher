using Configurator.Application.Services.Licensing;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Configurator.LicenseIssuer;

public sealed class LicenseIssuerService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly TimeProvider _timeProvider;

    public LicenseIssuerService(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task GenerateKeyAsync(
        string keyId,
        string publicKeyPath,
        string privateKeyPath,
        bool isTestKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPath);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = new TrustedLicensePublicKey
        {
            KeyId = keyId,
            PublicKeyPem = ecdsa.ExportSubjectPublicKeyInfoPem(),
            IsTestKey = isTestKey
        };

        await WriteTextAsync(
            publicKeyPath,
            JsonSerializer.Serialize(publicKey, LicenseJson.IndentedOptions),
            cancellationToken).ConfigureAwait(false);
        await WriteTextAsync(
            privateKeyPath,
            ecdsa.ExportPkcs8PrivateKeyPem(),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<LicenseEnvelope> IssueAsync(
        string profilePath,
        string privateKeyPath,
        string keyId,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var profileJson = await File.ReadAllTextAsync(profilePath, cancellationToken).ConfigureAwait(false);
        var profile = JsonSerializer.Deserialize<LicenseIssuerProfile>(profileJson, LicenseJson.Options)
            ?? throw new InvalidDataException("License profile is empty.");
        var payload = profile.ToPayload(_timeProvider.GetUtcNow().ToUniversalTime());
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, LicenseJson.Options);
        var signature = SignPayload(privateKeyPath, payloadBytes);
        var envelope = new LicenseEnvelope
        {
            Format = LicenseConstants.EnvelopeFormat,
            SchemaVersion = LicenseConstants.SchemaVersion,
            Algorithm = LicenseConstants.Algorithm,
            KeyId = keyId,
            Payload = Base64UrlCodec.Encode(payloadBytes),
            Signature = Base64UrlCodec.Encode(signature)
        };

        await WriteTextAsync(
            outputPath,
            JsonSerializer.Serialize(envelope, LicenseJson.IndentedOptions),
            cancellationToken).ConfigureAwait(false);

        return envelope;
    }

    public async Task<LicenseValidationResult> VerifyAsync(
        string licensePath,
        string publicKeyPath,
        bool allowTestKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(licensePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPath);

        var publicKeyJson = await File.ReadAllTextAsync(publicKeyPath, cancellationToken).ConfigureAwait(false);
        var publicKey = JsonSerializer.Deserialize<TrustedLicensePublicKey>(publicKeyJson, LicenseJson.Options)
            ?? throw new InvalidDataException("Public key file is empty.");
        var licenseBytes = await File.ReadAllBytesAsync(licensePath, cancellationToken).ConfigureAwait(false);
        var installationId = TryReadInstallationIdHint(licenseBytes) ?? "issuer-verify";
        var options = new LicensingOptions
        {
            AllowTestKeys = allowTestKey,
            TrustedPublicKeys = [publicKey]
        };
        var verifier = new OfflineLicenseVerifier(
            options,
            new StaticInstallationIdentityService(installationId),
            new InMemoryTrustedTimeStateStore(),
            _timeProvider);

        return await verifier.VerifyAsync(licenseBytes, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> InspectAsync(string licensePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(licensePath);

        var licenseJson = await File.ReadAllTextAsync(licensePath, cancellationToken).ConfigureAwait(false);
        var envelope = JsonSerializer.Deserialize<LicenseEnvelope>(licenseJson, LicenseJson.Options)
            ?? throw new InvalidDataException("License envelope is empty.");
        if (!Base64UrlCodec.TryDecode(envelope.Payload, out var payloadBytes))
        {
            throw new InvalidDataException("License payload is not valid Base64Url.");
        }

        using var document = JsonDocument.Parse(payloadBytes);

        return JsonSerializer.Serialize(document.RootElement, LicenseJson.IndentedOptions);
    }

    private static byte[] SignPayload(string privateKeyPath, byte[] payloadBytes)
    {
        var privateKeyPem = File.ReadAllText(privateKeyPath);
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);
        if (ecdsa.KeySize != 256)
        {
            throw new InvalidDataException("Private key must be ECDSA P-256.");
        }

        return ecdsa.SignData(
            payloadBytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private static async Task WriteTextAsync(
        string path,
        string text,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        await File.WriteAllTextAsync(path, text, Utf8NoBom, cancellationToken).ConfigureAwait(false);
    }

    private static string? TryReadInstallationIdHint(byte[] licenseBytes)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<LicenseEnvelope>(licenseBytes, LicenseJson.Options);
            if (envelope is null || !Base64UrlCodec.TryDecode(envelope.Payload, out var payloadBytes))
            {
                return null;
            }

            var payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes, LicenseJson.Options);

            return string.IsNullOrWhiteSpace(payload?.Installation.InstallationId)
                ? null
                : payload.Installation.InstallationId;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class StaticInstallationIdentityService(string installationId) : IInstallationIdentityService
    {
        public Task<InstallationIdentity> GetOrCreateAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new InstallationIdentity(installationId, DateTimeOffset.UnixEpoch));
        }

        public async Task<InstallationIdentityRequest> CreateRequestAsync(CancellationToken cancellationToken = default)
        {
            var identity = await GetOrCreateAsync(cancellationToken).ConfigureAwait(false);

            return new InstallationIdentityRequest(
                LicenseConstants.RequestFormat,
                LicenseConstants.Product,
                identity.InstallationId,
                identity.CreatedAtUtc,
                DateTimeOffset.UnixEpoch);
        }
    }

    private sealed class InMemoryTrustedTimeStateStore : ITrustedTimeStateStore
    {
        private TrustedTimeState? _state;

        public Task<TrustedTimeState?> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(_state);
        }

        public Task WriteAsync(TrustedTimeState state, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _state = state;

            return Task.CompletedTask;
        }
    }
}
