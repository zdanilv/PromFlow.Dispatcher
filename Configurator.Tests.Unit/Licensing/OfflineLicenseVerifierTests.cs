using Configurator.Application.Services.Licensing;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Configurator.Tests.Unit.Licensing;

public sealed class OfflineLicenseVerifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Verify_ValidLicense_SucceedsAndUpdatesTrustedTime()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var store = new MemoryTrustedTimeStateStore();
        var verifier = CreateVerifier(key, store: store);

        var result = await verifier.VerifyAsync(CreateLicense(key));

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Equal(LicenseStatus.Valid, result.Status);
        Assert.Equal(Now, store.State!.MaxObservedUtc);
    }

    [Fact]
    public async Task Verify_TamperedPayload_DeniesInvalidSignature()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var license = MutateEnvelope(CreateLicense(key), envelope =>
        {
            Base64UrlCodec.TryDecode(envelope.Payload, out var payloadBytes);
            payloadBytes[0] ^= 0x01;
            envelope.Payload = Base64UrlCodec.Encode(payloadBytes);
        });
        var verifier = CreateVerifier(key);

        var result = await verifier.VerifyAsync(license);

        AssertFailure(result, LicenseStatus.Invalid, LicenseValidationErrorCode.InvalidSignature);
    }

    [Fact]
    public async Task Verify_TamperedSignature_DeniesInvalidSignature()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var license = MutateEnvelope(CreateLicense(key), envelope =>
        {
            Base64UrlCodec.TryDecode(envelope.Signature, out var signatureBytes);
            signatureBytes[0] ^= 0x01;
            envelope.Signature = Base64UrlCodec.Encode(signatureBytes);
        });
        var verifier = CreateVerifier(key);

        var result = await verifier.VerifyAsync(license);

        AssertFailure(result, LicenseStatus.Invalid, LicenseValidationErrorCode.InvalidSignature);
    }

    [Fact]
    public async Task Verify_UnknownKey_DeniesBeforePayloadParsing()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var trustedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = CreateVerifier(trustedKey, keyId: "other-key");
        var invalidPayloadLicense = MutateEnvelope(CreateLicense(signingKey), envelope => envelope.Payload = "not*base64url");

        var result = await verifier.VerifyAsync(invalidPayloadLicense);

        AssertFailure(result, LicenseStatus.Invalid, LicenseValidationErrorCode.UnknownKeyId);
    }

    [Theory]
    [InlineData("format", LicenseValidationErrorCode.InvalidEnvelopeFormat)]
    [InlineData("schema", LicenseValidationErrorCode.UnsupportedSchemaVersion)]
    [InlineData("algorithm", LicenseValidationErrorCode.UnsupportedAlgorithm)]
    public async Task Verify_InvalidEnvelope_DeniesWithTypedError(string mutation, LicenseValidationErrorCode errorCode)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var license = MutateEnvelope(CreateLicense(key), envelope =>
        {
            if (mutation == "format")
            {
                envelope.Format = "Other";
            }
            else if (mutation == "schema")
            {
                envelope.SchemaVersion = 99;
            }
            else
            {
                envelope.Algorithm = "Other";
            }
        });
        var verifier = CreateVerifier(key);

        var result = await verifier.VerifyAsync(license);

        AssertFailure(result, LicenseStatus.Invalid, errorCode);
    }

    [Fact]
    public async Task Verify_InvalidPayloadJsonAfterValidSignature_DeniesPayloadJson()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var payloadBytes = Encoding.UTF8.GetBytes("{\"broken\":");
        var license = CreateLicenseFromPayloadBytes(key, payloadBytes);
        var verifier = CreateVerifier(key);

        var result = await verifier.VerifyAsync(license);

        AssertFailure(result, LicenseStatus.Invalid, LicenseValidationErrorCode.InvalidPayloadJson);
    }

    [Fact]
    public async Task Verify_DateProductVersionAndInstallationFailures_AreTyped()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = CreateVerifier(key);

        var expired = await verifier.VerifyAsync(CreateLicense(key, payload => payload.ExpiresAtUtc = Now.AddDays(-1)));
        var notYetValid = await verifier.VerifyAsync(CreateLicense(key, payload => payload.ValidFromUtc = Now.AddDays(1)));
        var wrongProduct = await verifier.VerifyAsync(CreateLicense(key, payload => payload.Product = "Other"));
        var wrongVersion = await verifier.VerifyAsync(CreateLicense(key, payload =>
        {
            payload.ProductVersion.Minimum = "2.0.0";
            payload.ProductVersion.MaximumExclusive = "3.0.0";
        }));
        var wrongInstallation = await verifier.VerifyAsync(CreateLicense(key, payload => payload.Installation.InstallationId = "other"));

        AssertFailure(expired, LicenseStatus.Expired, LicenseValidationErrorCode.Expired);
        AssertFailure(notYetValid, LicenseStatus.NotYetValid, LicenseValidationErrorCode.NotYetValid);
        AssertFailure(wrongProduct, LicenseStatus.ProductMismatch, LicenseValidationErrorCode.ProductMismatch);
        AssertFailure(wrongVersion, LicenseStatus.ProductVersionMismatch, LicenseValidationErrorCode.ProductVersionUnsupported);
        AssertFailure(wrongInstallation, LicenseStatus.InstallationMismatch, LicenseValidationErrorCode.InstallationMismatch);
    }

    [Fact]
    public async Task Verify_MalformedOversizedRollbackAndEditionFailures_AreTyped()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var oversizedVerifier = CreateVerifier(key, options: new LicensingOptions { MaxLicenseFileBytes = 4 });
        var rollbackStore = new MemoryTrustedTimeStateStore { State = new TrustedTimeState(Now.AddHours(1), Now.AddHours(1)) };
        var rollbackVerifier = CreateVerifier(key, store: rollbackStore);
        var featureVerifier = CreateVerifier(key);

        var malformed = await featureVerifier.VerifyAsync([0xff, 0xfe]);
        var oversized = await oversizedVerifier.VerifyAsync(CreateLicense(key));
        var rollback = await rollbackVerifier.VerifyAsync(CreateLicense(key));
        var inconsistent = await featureVerifier.VerifyAsync(CreateLicense(key, payload =>
        {
            payload.Edition = LicenseEdition.Community;
            payload.Features = [LicenseFeature.RouteMap, LicenseFeature.ArchiveExport];
        }));

        AssertFailure(malformed, LicenseStatus.Invalid, LicenseValidationErrorCode.InvalidUtf8);
        AssertFailure(oversized, LicenseStatus.Invalid, LicenseValidationErrorCode.FileTooLarge);
        AssertFailure(rollback, LicenseStatus.ClockRollbackDetected, LicenseValidationErrorCode.ClockRollbackDetected);
        AssertFailure(inconsistent, LicenseStatus.Invalid, LicenseValidationErrorCode.FeatureEditionInconsistent);
    }

    [Fact]
    public async Task Verify_KeyRotationAndTestKeyPolicy_AreEnforced()
    {
        using var first = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var second = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rotated = CreateVerifier(second, extraKeys: [CreateTrustedKey(first, "old-key")]);
        var production = CreateVerifier(second, trustedKeyOverride: CreateTrustedKey(second, isTestKey: true));
        var testMode = CreateVerifier(
            second,
            options: new LicensingOptions { AllowTestKeys = true },
            trustedKeyOverride: CreateTrustedKey(second, isTestKey: true));

        var rotationResult = await rotated.VerifyAsync(CreateLicense(second));
        var productionResult = await production.VerifyAsync(CreateLicense(second));
        var testModeResult = await testMode.VerifyAsync(CreateLicense(second));

        Assert.True(rotationResult.Succeeded);
        AssertFailure(productionResult, LicenseStatus.Invalid, LicenseValidationErrorCode.TestKeyRejected);
        Assert.True(testModeResult.Succeeded);
    }

    private static OfflineLicenseVerifier CreateVerifier(
        ECDsa trustedKey,
        string keyId = "stage10-key",
        LicensingOptions? options = null,
        ITrustedTimeStateStore? store = null,
        TrustedLicensePublicKey? trustedKeyOverride = null,
        IReadOnlyList<TrustedLicensePublicKey>? extraKeys = null)
    {
        var effectiveOptions = options?.Clone() ?? new LicensingOptions();
        effectiveOptions.TrustedPublicKeys = [trustedKeyOverride ?? CreateTrustedKey(trustedKey, keyId)];
        if (extraKeys is not null)
        {
            effectiveOptions.TrustedPublicKeys.AddRange(extraKeys);
        }

        return new OfflineLicenseVerifier(
            effectiveOptions,
            new FixedInstallationIdentityService("installation-1"),
            store ?? new MemoryTrustedTimeStateStore(),
            new FixedTimeProvider(Now));
    }

    internal static TrustedLicensePublicKey CreateTrustedKey(
        ECDsa key,
        string keyId = "stage10-key",
        bool isTestKey = false)
        => new()
        {
            KeyId = keyId,
            PublicKeyPem = key.ExportSubjectPublicKeyInfoPem(),
            IsTestKey = isTestKey
        };

    internal static byte[] CreateLicense(ECDsa key, Action<LicensePayload>? mutatePayload = null)
    {
        var payload = CreatePayload();
        mutatePayload?.Invoke(payload);
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, LicenseJson.Options);

        return CreateLicenseFromPayloadBytes(key, payloadBytes);
    }

    internal static LicensePayload CreatePayload()
        => new()
        {
            LicenseId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Product = LicenseConstants.Product,
            IssuedAtUtc = Now.AddDays(-1),
            ValidFromUtc = Now.AddDays(-1),
            ExpiresAtUtc = Now.AddYears(1),
            Edition = LicenseEdition.Professional,
            LicenseVersion = LicenseConstants.LicenseVersion,
            ProductVersion = new LicenseProductVersionRange { Minimum = "1.0.0", MaximumExclusive = "2.0.0" },
            Features = [LicenseFeature.RouteMap, LicenseFeature.RemoteControl, LicenseFeature.Archive, LicenseFeature.ArchiveExport],
            Customer = new LicenseCustomerProfile { FullName = "Test Customer", Email = "test@example.invalid", Phone = "+10000000000" },
            Organization = new LicenseOrganizationProfile { Name = "Test Org", SiteAddress = "Test Site" },
            Installation = new LicenseInstallationProfile
            {
                BindingMode = LicenseInstallationBindingMode.InstallationId,
                InstallationId = "installation-1"
            }
        };

    internal static byte[] CreateLicenseFromPayloadBytes(ECDsa key, byte[] payloadBytes, string keyId = "stage10-key")
    {
        var signature = key.SignData(
            payloadBytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var envelope = new LicenseEnvelope
        {
            Format = LicenseConstants.EnvelopeFormat,
            SchemaVersion = LicenseConstants.SchemaVersion,
            Algorithm = LicenseConstants.Algorithm,
            KeyId = keyId,
            Payload = Base64UrlCodec.Encode(payloadBytes),
            Signature = Base64UrlCodec.Encode(signature)
        };

        return JsonSerializer.SerializeToUtf8Bytes(envelope, LicenseJson.Options);
    }

    private static byte[] MutateEnvelope(byte[] licenseBytes, Action<LicenseEnvelope> mutate)
    {
        var envelope = JsonSerializer.Deserialize<LicenseEnvelope>(licenseBytes, LicenseJson.Options)!;
        mutate(envelope);

        return JsonSerializer.SerializeToUtf8Bytes(envelope, LicenseJson.Options);
    }

    private static void AssertFailure(
        LicenseValidationResult result,
        LicenseStatus status,
        LicenseValidationErrorCode code)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(status, result.Status);
        Assert.Contains(result.Errors, error => error.Code == code);
    }

    internal sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }

    internal sealed class FixedInstallationIdentityService(string installationId) : IInstallationIdentityService
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

    internal sealed class MemoryTrustedTimeStateStore : ITrustedTimeStateStore
    {
        public TrustedTimeState? State { get; set; }

        public Task<TrustedTimeState?> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(State);
        }

        public Task WriteAsync(TrustedTimeState state, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            State = state;

            return Task.CompletedTask;
        }
    }
}
