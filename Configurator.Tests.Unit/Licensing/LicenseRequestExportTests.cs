using System.Text.Json;
using Configurator.Application.Services.Licensing;
using Configurator.Infrastructure.Licensing;
using Xunit;

namespace Configurator.Tests.Unit.Licensing;

public sealed class LicenseRequestExportTests : IDisposable
{
    private readonly string _directoryPath = Path.Combine(
        Path.GetTempPath(),
        "PromFlow.LicenseRequestExportTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Export_WritesInstallationRequestAtomically()
    {
        var service = new FileLicenseRequestExportService();
        var request = new InstallationIdentityRequest(
            LicenseConstants.RequestFormat,
            LicenseConstants.Product,
            "installation-1",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);
        var path = Path.Combine(_directoryPath, "installation.promrequest");

        var result = await service.ExportAsync(request, path);

        Assert.True(result.Succeeded);
        var loaded = JsonSerializer.Deserialize<InstallationIdentityRequest>(
            await File.ReadAllBytesAsync(path),
            LicenseJson.Options);
        Assert.Equal(request.InstallationId, loaded!.InstallationId);
        Assert.DoesNotContain("signature", await File.ReadAllTextAsync(path), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directoryPath))
            {
                Directory.Delete(_directoryPath, recursive: true);
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
