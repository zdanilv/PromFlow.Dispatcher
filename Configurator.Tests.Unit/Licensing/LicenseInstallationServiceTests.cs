using Configurator.Application.Services.Licensing;
using Xunit;

namespace Configurator.Tests.Unit.Licensing;

public sealed class LicenseInstallationServiceTests
{
    [Fact]
    public async Task Install_ValidLicense_WritesOnceUpdatesCurrentAndRaisesEvent()
    {
        var payload = Payload(LicenseFeature.RouteMap);
        var validation = LicenseValidationResult.Success(Envelope(), payload);
        var writable = new RecordingWritableStore();
        var service = new DefaultLicenseService(
            new StaticStore(LicenseStoreReadResult.Missing()),
            writable,
            new StaticVerifier(validation),
            new OfflineLicenseVerifierTests.FixedTimeProvider(DateTimeOffset.UnixEpoch),
            [],
            []);
        var events = 0;
        service.StateChanged += (_, _) => events++;

        var result = await service.InstallAsync(new LicenseInstallRequest([1, 2, 3], "valid.promlicense"));

        Assert.True(result.Succeeded);
        Assert.Equal(1, writable.WriteCount);
        Assert.Equal(LicenseStatus.Valid, service.Current.Status);
        Assert.Equal(payload.LicenseId.ToString("D"), result.NewLicenseId);
        Assert.Equal(1, events);
    }

    [Fact]
    public async Task Install_InvalidLicense_DoesNotWriteAndKeepsCurrentState()
    {
        var writable = new RecordingWritableStore();
        var service = new DefaultLicenseService(
            new StaticStore(LicenseStoreReadResult.Missing()),
            writable,
            new StaticVerifier(LicenseValidationResult.Failure(
                LicenseStatus.Invalid,
                LicenseValidationErrorCode.InvalidSignature,
                "bad signature")),
            new OfflineLicenseVerifierTests.FixedTimeProvider(DateTimeOffset.UnixEpoch),
            [],
            []);

        var result = await service.InstallAsync(new LicenseInstallRequest([1, 2, 3], "bad.promlicense"));

        Assert.False(result.Succeeded);
        Assert.Equal(LicenseValidationErrorCode.InvalidSignature, result.ReasonCode);
        Assert.Equal(0, writable.WriteCount);
        Assert.Equal(LicenseStatus.Missing, service.Current.Status);
    }

    [Fact]
    public async Task Install_StorageFailure_DoesNotReplaceCurrentState()
    {
        var firstPayload = Payload(LicenseFeature.RouteMap);
        var secondPayload = Payload(LicenseFeature.Diagnostics);
        var writable = new RecordingWritableStore
        {
            Result = LicenseStoreWriteResult.Success()
        };
        var verifier = new MutableVerifier(LicenseValidationResult.Success(Envelope(), firstPayload));
        var service = new DefaultLicenseService(
            new StaticStore(LicenseStoreReadResult.Missing()),
            writable,
            verifier,
            new OfflineLicenseVerifierTests.FixedTimeProvider(DateTimeOffset.UnixEpoch),
            [],
            []);

        var first = await service.InstallAsync(new LicenseInstallRequest([1], "first.promlicense"));
        verifier.Result = LicenseValidationResult.Success(Envelope(), secondPayload);
        writable.Result = LicenseStoreWriteResult.Failure("StoreUnavailable", "disk locked");

        var result = await service.InstallAsync(new LicenseInstallRequest([2], "second.promlicense"));

        Assert.True(first.Succeeded);
        Assert.False(result.Succeeded);
        Assert.Equal(LicenseValidationErrorCode.StoreUnavailable, result.ReasonCode);
        Assert.Equal(firstPayload.LicenseId, service.Current.Payload!.LicenseId);
    }

    private static LicensePayload Payload(params string[] features)
        => new()
        {
            LicenseId = Guid.NewGuid(),
            Product = LicenseConstants.Product,
            IssuedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
            ValidFromUtc = DateTimeOffset.UtcNow.AddDays(-1),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
            Edition = LicenseEdition.Professional,
            LicenseVersion = LicenseConstants.LicenseVersion,
            ProductVersion = new LicenseProductVersionRange(),
            Features = [.. features],
            Installation = new LicenseInstallationProfile
            {
                BindingMode = LicenseInstallationBindingMode.InstallationId,
                InstallationId = "installation-1"
            }
        };

    private static LicenseEnvelope Envelope()
        => new()
        {
            Format = LicenseConstants.EnvelopeFormat,
            SchemaVersion = LicenseConstants.SchemaVersion,
            Algorithm = LicenseConstants.Algorithm,
            KeyId = "test-key",
            Payload = "payload",
            Signature = "signature"
        };

    private sealed class StaticStore(LicenseStoreReadResult result) : ILicenseStore
    {
        public Task<LicenseStoreReadResult> ReadCurrentAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingWritableStore : ILicenseWritableStore
    {
        public LicenseStoreWriteResult Result { get; set; } = LicenseStoreWriteResult.Success();
        public int WriteCount { get; private set; }

        public Task<LicenseStoreWriteResult> ReplaceCurrentAsync(
            byte[] licenseBytes,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteCount++;
            return Task.FromResult(Result);
        }
    }

    private sealed class StaticVerifier(LicenseValidationResult result) : ILicenseVerifier
    {
        public Task<LicenseValidationResult> VerifyAsync(
            byte[] licenseBytes,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }

    private sealed class MutableVerifier(LicenseValidationResult result) : ILicenseVerifier
    {
        public LicenseValidationResult Result { get; set; } = result;

        public Task<LicenseValidationResult> VerifyAsync(
            byte[] licenseBytes,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Result);
        }
    }
}
