using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Configurator.Application.Services.Licensing;

public sealed class OfflineLicenseVerifier : ILicenseVerifier
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly LicensingOptions _options;
    private readonly IInstallationIdentityService _installationIdentityService;
    private readonly ITrustedTimeStateStore _trustedTimeStateStore;
    private readonly TimeProvider _timeProvider;

    public OfflineLicenseVerifier(
        LicensingOptions options,
        IInstallationIdentityService installationIdentityService,
        ITrustedTimeStateStore trustedTimeStateStore,
        TimeProvider timeProvider)
    {
        _options = options?.Clone() ?? throw new ArgumentNullException(nameof(options));
        _installationIdentityService = installationIdentityService ?? throw new ArgumentNullException(nameof(installationIdentityService));
        _trustedTimeStateStore = trustedTimeStateStore ?? throw new ArgumentNullException(nameof(trustedTimeStateStore));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<LicenseValidationResult> VerifyAsync(
        byte[] licenseBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(licenseBytes);
        cancellationToken.ThrowIfCancellationRequested();

        if (licenseBytes.Length > Math.Max(1, _options.MaxLicenseFileBytes))
        {
            return Failure(
                LicenseStatus.Invalid,
                LicenseValidationErrorCode.FileTooLarge,
                "License file is larger than the configured limit.");
        }

        if (!TryDecodeUtf8(licenseBytes, out var envelopeJson))
        {
            return Failure(
                LicenseStatus.Invalid,
                LicenseValidationErrorCode.InvalidUtf8,
                "License envelope is not valid UTF-8.");
        }

        if (!TryDeserializeEnvelope(envelopeJson, out var envelope))
        {
            return Failure(
                LicenseStatus.Invalid,
                LicenseValidationErrorCode.InvalidJson,
                "License envelope is not valid JSON.");
        }

        var envelopeValidation = ValidateEnvelope(envelope);
        if (envelopeValidation is not null)
        {
            return envelopeValidation;
        }

        var trustedKey = _options.TrustedPublicKeys.FirstOrDefault(
            key => string.Equals(key.KeyId, envelope.KeyId, StringComparison.Ordinal));
        if (trustedKey is null)
        {
            return Failure(
                LicenseStatus.Invalid,
                LicenseValidationErrorCode.UnknownKeyId,
                "License key id is not trusted.",
                nameof(LicenseEnvelope.KeyId),
                envelope);
        }

        if (trustedKey.IsTestKey && !_options.AllowTestKeys)
        {
            return Failure(
                LicenseStatus.Invalid,
                LicenseValidationErrorCode.TestKeyRejected,
                "Test license public key is not trusted by the production verifier.",
                nameof(TrustedLicensePublicKey.IsTestKey),
                envelope);
        }

        if (!Base64UrlCodec.TryDecode(envelope.Payload, out var payloadBytes)
            || !Base64UrlCodec.TryDecode(envelope.Signature, out var signatureBytes))
        {
            return Failure(
                LicenseStatus.Invalid,
                LicenseValidationErrorCode.InvalidBase64Url,
                "License payload or signature is not valid Base64Url.",
                envelope: envelope);
        }

        if (!VerifySignature(trustedKey.PublicKeyPem, payloadBytes, signatureBytes))
        {
            return Failure(
                LicenseStatus.Invalid,
                LicenseValidationErrorCode.InvalidSignature,
                "License signature is invalid.",
                envelope: envelope);
        }

        if (!TryDeserializePayload(payloadBytes, out var payload))
        {
            return Failure(
                LicenseStatus.Invalid,
                LicenseValidationErrorCode.InvalidPayloadJson,
                "License payload is not valid JSON.",
                envelope: envelope);
        }

        var semanticValidation = await ValidatePayloadAsync(payload, envelope, cancellationToken).ConfigureAwait(false);
        if (semanticValidation is not null)
        {
            return semanticValidation;
        }

        var nowUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        try
        {
            await UpdateTrustedTimeAsync(nowUtc, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Failure(
                LicenseStatus.StoreUnavailable,
                LicenseValidationErrorCode.StoreUnavailable,
                "Trusted time state could not be updated.",
                envelope: envelope);
        }

        return LicenseValidationResult.Success(envelope, payload);
    }

    private async Task<LicenseValidationResult?> ValidatePayloadAsync(
        LicensePayload payload,
        LicenseEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var semantic = ValidateSemanticFields(payload);
        if (semantic is not null)
        {
            return semantic(envelope);
        }

        if (!string.Equals(payload.Product, _options.Product, StringComparison.Ordinal))
        {
            return Failure(
                LicenseStatus.ProductMismatch,
                LicenseValidationErrorCode.ProductMismatch,
                "License product does not match this application.",
                nameof(LicensePayload.Product),
                envelope);
        }

        var versionValidation = ValidateProductVersion(payload, envelope);
        if (versionValidation is not null)
        {
            return versionValidation;
        }

        var nowUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        var skew = TimeSpan.FromMinutes(Math.Max(0, _options.AllowedClockSkewMinutes));
        if (nowUtc.Add(skew) < payload.ValidFromUtc.ToUniversalTime())
        {
            return Failure(
                LicenseStatus.NotYetValid,
                LicenseValidationErrorCode.NotYetValid,
                "License is not valid yet.",
                nameof(LicensePayload.ValidFromUtc),
                envelope);
        }

        if (nowUtc.Subtract(skew) > payload.ExpiresAtUtc.ToUniversalTime())
        {
            return Failure(
                LicenseStatus.Expired,
                LicenseValidationErrorCode.Expired,
                "License is expired.",
                nameof(LicensePayload.ExpiresAtUtc),
                envelope);
        }

        var installationValidation = await ValidateInstallationAsync(payload, envelope, cancellationToken)
            .ConfigureAwait(false);
        if (installationValidation is not null)
        {
            return installationValidation;
        }

        var rollbackValidation = await ValidateClockRollbackAsync(nowUtc, skew, envelope, cancellationToken)
            .ConfigureAwait(false);
        if (rollbackValidation is not null)
        {
            return rollbackValidation;
        }

        if (!LicenseEditionPolicy.Allows(payload.Edition, payload.Features))
        {
            return Failure(
                LicenseStatus.Invalid,
                LicenseValidationErrorCode.FeatureEditionInconsistent,
                "License features are not allowed by the selected edition.",
                nameof(LicensePayload.Features),
                envelope);
        }

        return null;
    }

    private Func<LicenseEnvelope, LicenseValidationResult>? ValidateSemanticFields(LicensePayload payload)
    {
        if (payload.LicenseId == Guid.Empty)
        {
            return envelope => SemanticFailure("License id must not be empty.", nameof(LicensePayload.LicenseId), envelope);
        }

        if (string.IsNullOrWhiteSpace(payload.Product))
        {
            return envelope => SemanticFailure("License product must not be empty.", nameof(LicensePayload.Product), envelope);
        }

        if (payload.LicenseVersion != LicenseConstants.LicenseVersion)
        {
            return envelope => SemanticFailure("License version is unsupported.", nameof(LicensePayload.LicenseVersion), envelope);
        }

        if (!Enum.IsDefined(payload.Edition))
        {
            return envelope => SemanticFailure("License edition is unsupported.", nameof(LicensePayload.Edition), envelope);
        }

        if (!Enum.IsDefined(payload.Installation.BindingMode))
        {
            return envelope => SemanticFailure("License installation binding mode is unsupported.", nameof(LicenseInstallationProfile.BindingMode), envelope);
        }

        if (payload.ValidFromUtc.ToUniversalTime() > payload.ExpiresAtUtc.ToUniversalTime())
        {
            return envelope => SemanticFailure("License validity range is invalid.", nameof(LicensePayload.ValidFromUtc), envelope);
        }

        if (payload.Features.Any(string.IsNullOrWhiteSpace)
            || payload.Features.Distinct(StringComparer.Ordinal).Count() != payload.Features.Count
            || payload.Features.Any(feature => !LicenseFeature.Known.Contains(feature)))
        {
            return envelope => SemanticFailure("License feature list is invalid.", nameof(LicensePayload.Features), envelope);
        }

        return null;
    }

    private LicenseValidationResult? ValidateProductVersion(LicensePayload payload, LicenseEnvelope envelope)
    {
        if (!Version.TryParse(_options.ProductVersion, out var currentVersion)
            || !Version.TryParse(payload.ProductVersion.Minimum, out var minimumVersion)
            || !Version.TryParse(payload.ProductVersion.MaximumExclusive, out var maximumExclusiveVersion)
            || minimumVersion >= maximumExclusiveVersion)
        {
            return SemanticFailure("License product version range is invalid.", nameof(LicensePayload.ProductVersion), envelope);
        }

        if (currentVersion < minimumVersion || currentVersion >= maximumExclusiveVersion)
        {
            return Failure(
                LicenseStatus.ProductVersionMismatch,
                LicenseValidationErrorCode.ProductVersionUnsupported,
                "License does not support this application version.",
                nameof(LicensePayload.ProductVersion),
                envelope);
        }

        return null;
    }

    private async Task<LicenseValidationResult?> ValidateInstallationAsync(
        LicensePayload payload,
        LicenseEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (_options.BindingMode != LicenseInstallationBindingMode.None
            && payload.Installation.BindingMode != _options.BindingMode)
        {
            return Failure(
                LicenseStatus.InstallationMismatch,
                LicenseValidationErrorCode.InstallationMismatch,
                "License installation binding mode does not match application policy.",
                nameof(LicenseInstallationProfile.BindingMode),
                envelope);
        }

        if (payload.Installation.BindingMode == LicenseInstallationBindingMode.None)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(payload.Installation.InstallationId))
        {
            return SemanticFailure("License installation id must not be empty.", nameof(LicenseInstallationProfile.InstallationId), envelope);
        }

        var identity = await _installationIdentityService.GetOrCreateAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(identity.InstallationId, payload.Installation.InstallationId, StringComparison.Ordinal))
        {
            return Failure(
                LicenseStatus.InstallationMismatch,
                LicenseValidationErrorCode.InstallationMismatch,
                "License is bound to another installation.",
                nameof(LicenseInstallationProfile.InstallationId),
                envelope);
        }

        return null;
    }

    private async Task<LicenseValidationResult?> ValidateClockRollbackAsync(
        DateTimeOffset nowUtc,
        TimeSpan skew,
        LicenseEnvelope envelope,
        CancellationToken cancellationToken)
    {
        TrustedTimeState? state;
        try
        {
            state = await _trustedTimeStateStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Failure(
                LicenseStatus.StoreUnavailable,
                LicenseValidationErrorCode.StoreUnavailable,
                "Trusted time state could not be read.",
                envelope: envelope);
        }

        if (state is not null && nowUtc.Add(skew) < state.MaxObservedUtc.ToUniversalTime())
        {
            return Failure(
                LicenseStatus.ClockRollbackDetected,
                LicenseValidationErrorCode.ClockRollbackDetected,
                "System clock appears to have moved backwards.",
                nameof(TrustedTimeState.MaxObservedUtc),
                envelope);
        }

        return null;
    }

    private async Task UpdateTrustedTimeAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var current = await _trustedTimeStateStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        var maxObservedUtc = current is null || nowUtc > current.MaxObservedUtc.ToUniversalTime()
            ? nowUtc
            : current.MaxObservedUtc.ToUniversalTime();

        await _trustedTimeStateStore
            .WriteAsync(new TrustedTimeState(maxObservedUtc, nowUtc), cancellationToken)
            .ConfigureAwait(false);
    }

    private static LicenseValidationResult? ValidateEnvelope(LicenseEnvelope envelope)
    {
        if (!string.Equals(envelope.Format, LicenseConstants.EnvelopeFormat, StringComparison.Ordinal))
        {
            return Failure(
                LicenseStatus.Invalid,
                LicenseValidationErrorCode.InvalidEnvelopeFormat,
                "License envelope format is unsupported.",
                nameof(LicenseEnvelope.Format),
                envelope);
        }

        if (envelope.SchemaVersion != LicenseConstants.SchemaVersion)
        {
            return Failure(
                LicenseStatus.Invalid,
                LicenseValidationErrorCode.UnsupportedSchemaVersion,
                "License envelope schema version is unsupported.",
                nameof(LicenseEnvelope.SchemaVersion),
                envelope);
        }

        if (!string.Equals(envelope.Algorithm, LicenseConstants.Algorithm, StringComparison.Ordinal))
        {
            return Failure(
                LicenseStatus.Invalid,
                LicenseValidationErrorCode.UnsupportedAlgorithm,
                "License signature algorithm is unsupported.",
                nameof(LicenseEnvelope.Algorithm),
                envelope);
        }

        if (string.IsNullOrWhiteSpace(envelope.KeyId))
        {
            return Failure(
                LicenseStatus.Invalid,
                LicenseValidationErrorCode.InvalidEnvelopeFormat,
                "License key id is required.",
                nameof(LicenseEnvelope.KeyId),
                envelope);
        }

        if (string.IsNullOrWhiteSpace(envelope.Payload) || string.IsNullOrWhiteSpace(envelope.Signature))
        {
            return Failure(
                LicenseStatus.Invalid,
                LicenseValidationErrorCode.InvalidEnvelopeFormat,
                "License payload and signature are required.",
                envelope: envelope);
        }

        return null;
    }

    private static bool VerifySignature(string publicKeyPem, byte[] payloadBytes, byte[] signatureBytes)
    {
        if (signatureBytes.Length != LicenseConstants.EcdsaP256SignatureLength
            || string.IsNullOrWhiteSpace(publicKeyPem))
        {
            return false;
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPem);
            if (ecdsa.KeySize != 256)
            {
                return false;
            }

            return ecdsa.VerifyData(
                payloadBytes,
                signatureBytes,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryDecodeUtf8(byte[] bytes, out string value)
    {
        try
        {
            value = StrictUtf8.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            value = string.Empty;
            return false;
        }
    }

    private static bool TryDeserializeEnvelope(string json, out LicenseEnvelope envelope)
    {
        try
        {
            envelope = JsonSerializer.Deserialize<LicenseEnvelope>(json, LicenseJson.Options) ?? new LicenseEnvelope();
            return true;
        }
        catch (JsonException)
        {
            envelope = new LicenseEnvelope();
            return false;
        }
    }

    private static bool TryDeserializePayload(byte[] payloadBytes, out LicensePayload payload)
    {
        try
        {
            payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes, LicenseJson.Options) ?? new LicensePayload();
            return true;
        }
        catch (JsonException)
        {
            payload = new LicensePayload();
            return false;
        }
    }

    private static LicenseValidationResult SemanticFailure(string message, string field, LicenseEnvelope envelope)
        => Failure(
            LicenseStatus.Invalid,
            LicenseValidationErrorCode.SemanticFieldInvalid,
            message,
            field,
            envelope);

    private static LicenseValidationResult Failure(
        LicenseStatus status,
        LicenseValidationErrorCode code,
        string message,
        string? field = null,
        LicenseEnvelope? envelope = null)
        => LicenseValidationResult.Failure(status, code, message, field, envelope);
}
