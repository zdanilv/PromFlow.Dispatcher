using Configurator.Application.Services.Licensing;
using Configurator.LicenseIssuer;
using System.Text.Json;
using Xunit;

namespace Configurator.Tests.Unit.Licensing;

public sealed class LicenseIssuerTests : IDisposable
{
    private readonly string _directoryPath;

    public LicenseIssuerTests()
    {
        _directoryPath = Path.Combine(Path.GetTempPath(), "PromFlow.IssuerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directoryPath);
    }

    [Fact]
    public async Task Service_GeneratesKeyIssuesVerifiesAndInspectsLicense()
    {
        var service = new LicenseIssuerService(new OfflineLicenseVerifierTests.FixedTimeProvider(
            new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero)));
        var publicKeyPath = Path.Combine(_directoryPath, "public.json");
        var privateKeyPath = Path.Combine(_directoryPath, "private.pem");
        var profilePath = GetProfilePath();
        var licensePath = Path.Combine(_directoryPath, "valid.promlicense");

        await service.GenerateKeyAsync("test-stage10", publicKeyPath, privateKeyPath, isTestKey: true);
        var envelope = await service.IssueAsync(profilePath, privateKeyPath, "test-stage10", licensePath);
        var verify = await service.VerifyAsync(licensePath, publicKeyPath, allowTestKey: true);
        var inspect = await service.InspectAsync(licensePath);

        Assert.Equal("test-stage10", envelope.KeyId);
        Assert.True(verify.Succeeded, string.Join("; ", verify.Errors.Select(error => error.Message)));
        Assert.Contains("\"licenseId\"", inspect, StringComparison.Ordinal);
        Assert.Contains("Test Customer", inspect, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_ReturnsExpectedExitCodesForHelpSuccessAndBadArguments()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var cli = new LicenseIssuerCli(TextReader.Null, output, error);
        var publicKeyPath = Path.Combine(_directoryPath, "public.json");
        var privateKeyPath = Path.Combine(_directoryPath, "private.pem");

        var help = await cli.RunAsync(["--help"]);
        var generated = await cli.RunAsync([
            "generate-key",
            "--key-id",
            "test-stage10",
            "--public-key",
            publicKeyPath,
            "--private-key",
            privateKeyPath,
            "--test-key"]);
        var usage = await cli.RunAsync(["generate-key", "--key-id"]);

        Assert.Equal(LicenseIssuerExitCodes.Success, help);
        Assert.Equal(LicenseIssuerExitCodes.Success, generated);
        Assert.Equal(LicenseIssuerExitCodes.UsageError, usage);
        Assert.True(File.Exists(publicKeyPath));
        Assert.True(File.Exists(privateKeyPath));
    }

    [Fact]
    public async Task Cli_VerifyRejectsTestKeyWithoutExplicitAllowFlag()
    {
        var service = new LicenseIssuerService(new OfflineLicenseVerifierTests.FixedTimeProvider(
            new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero)));
        var publicKeyPath = Path.Combine(_directoryPath, "public.json");
        var privateKeyPath = Path.Combine(_directoryPath, "private.pem");
        var licensePath = Path.Combine(_directoryPath, "valid.promlicense");
        await service.GenerateKeyAsync("test-stage10", publicKeyPath, privateKeyPath, isTestKey: true);
        await service.IssueAsync(GetProfilePath(), privateKeyPath, "test-stage10", licensePath);

        var cli = new LicenseIssuerCli(TextReader.Null, new StringWriter(), new StringWriter(), service);
        var denied = await cli.RunAsync(["verify", "--license", licensePath, "--public-key", publicKeyPath]);
        var allowed = await cli.RunAsync([
            "verify",
            "--license",
            licensePath,
            "--public-key",
            publicKeyPath,
            "--allow-test-key"]);

        Assert.Equal(LicenseIssuerExitCodes.VerificationFailed, denied);
        Assert.Equal(LicenseIssuerExitCodes.Success, allowed);
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

    private static string GetProfilePath()
        => Path.Combine(FindRepositoryRoot(), "Configurator.Tests.Unit", "Licensing", "TestData", "valid-profile.json");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DesktopTemplate.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
