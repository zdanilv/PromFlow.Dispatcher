using Configurator.Application.Services.Licensing;
using Configurator.Infrastructure.Licensing;
using Xunit;

namespace Configurator.Tests.Unit.Licensing;

public sealed class LicenseInfrastructureTests : IDisposable
{
    private readonly string _directoryPath;

    public LicenseInfrastructureTests()
    {
        _directoryPath = Path.Combine(Path.GetTempPath(), "PromFlow.LicensingTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directoryPath);
    }

    [Fact]
    public async Task InstallationIdentity_IsRandomStableAndExportableAsRequest()
    {
        var options = Options();
        using var service = new FileInstallationIdentityService(
            new LicensePathProvider(options),
            options,
            new OfflineLicenseVerifierTests.FixedTimeProvider(new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero)));

        var first = await service.GetOrCreateAsync();
        var second = await service.GetOrCreateAsync();
        var request = await service.CreateRequestAsync();

        Assert.Equal(first.InstallationId, second.InstallationId);
        Assert.True(Base64UrlCodec.TryDecode(first.InstallationId, out var bytes));
        Assert.Equal(32, bytes.Length);
        Assert.Equal(LicenseConstants.RequestFormat, request.Format);
        Assert.Equal(first.InstallationId, request.InstallationId);
    }

    [Fact]
    public async Task TrustedTimeStateStore_PersistsMaxObservedUtc()
    {
        var options = Options();
        var provider = new LicensePathProvider(options);
        var state = new TrustedTimeState(
            new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero));

        using (var writer = new FileTrustedTimeStateStore(provider))
        {
            await writer.WriteAsync(state);
        }

        using var reader = new FileTrustedTimeStateStore(provider);
        var loaded = await reader.ReadAsync();

        Assert.Equal(state, loaded);
    }

    [Fact]
    public async Task LicenseStore_MissingAndOversizedFiles_AreBounded()
    {
        var options = Options();
        options.MaxLicenseFileBytes = 4;
        var provider = new LicensePathProvider(options);
        var store = new FileLicenseStore(provider, options);

        var missing = await store.ReadCurrentAsync();
        Directory.CreateDirectory(provider.GetLicenseDirectory());
        await File.WriteAllBytesAsync(provider.GetCurrentLicensePath(), [1, 2, 3, 4, 5, 6]);
        var oversized = await store.ReadCurrentAsync();

        Assert.True(missing.IsMissing);
        Assert.True(oversized.Succeeded);
        Assert.Equal(5, oversized.Bytes.Length);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private LicensingOptions Options()
        => new()
        {
            LicenseDirectory = _directoryPath,
            LicenseFileName = "current.promlicense",
            InstallationIdentityFileName = "installation.json",
            TrustedTimeStateFileName = "trusted-time.json"
        };
}
