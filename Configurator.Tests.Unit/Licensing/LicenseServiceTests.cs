using Configurator.Application.Services.Licensing;
using Xunit;

namespace Configurator.Tests.Unit.Licensing;

public sealed class LicenseServiceTests
{
    [Fact]
    public async Task GetCurrent_MissingStore_ReturnsMissingState()
    {
        var service = new DefaultLicenseService(
            new FakeLicenseStore(LicenseStoreReadResult.Missing()),
            new FakeVerifier(LicenseValidationResult.Failure(LicenseStatus.Invalid, LicenseValidationErrorCode.InvalidJson, "unused")),
            new OfflineLicenseVerifierTests.FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var state = await service.GetCurrentAsync();

        Assert.Equal(LicenseStatus.Missing, state.Status);
    }

    [Fact]
    public async Task GetCurrent_StoreFailure_ReturnsStoreUnavailable()
    {
        var service = new DefaultLicenseService(
            new FakeLicenseStore(LicenseStoreReadResult.Failure("io")),
            new FakeVerifier(LicenseValidationResult.Failure(LicenseStatus.Invalid, LicenseValidationErrorCode.InvalidJson, "unused")),
            new OfflineLicenseVerifierTests.FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var state = await service.GetCurrentAsync();

        Assert.Equal(LicenseStatus.StoreUnavailable, state.Status);
        Assert.Contains(state.Errors, error => error.Code == LicenseValidationErrorCode.StoreUnavailable);
    }

    [Fact]
    public async Task GetCurrent_FoundBytes_UsesVerifierResult()
    {
        var validation = LicenseValidationResult.Failure(
            LicenseStatus.Expired,
            LicenseValidationErrorCode.Expired,
            "expired");
        var service = new DefaultLicenseService(
            new FakeLicenseStore(LicenseStoreReadResult.Found([1, 2, 3])),
            new FakeVerifier(validation),
            new OfflineLicenseVerifierTests.FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var state = await service.GetCurrentAsync();

        Assert.Equal(LicenseStatus.Expired, state.Status);
    }

    private sealed class FakeLicenseStore(LicenseStoreReadResult result) : ILicenseStore
    {
        public Task<LicenseStoreReadResult> ReadCurrentAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(result);
        }
    }

    private sealed class FakeVerifier(LicenseValidationResult result) : ILicenseVerifier
    {
        public Task<LicenseValidationResult> VerifyAsync(byte[] licenseBytes, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(result);
        }
    }
}
