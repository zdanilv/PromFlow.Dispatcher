using Configurator.Application;
using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Licensing;
using Configurator.Infrastructure.Licensing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Configurator.Tests.Unit.Licensing;

public sealed class LicenseDependencyInjectionTests
{
    [Fact]
    public void AddApplication_ResolvesLicenseCoreAndStage11FeatureGate()
    {
        var services = new ServiceCollection().AddApplication();
        services.AddSingleton(new LicensingOptions
        {
            LicenseDirectory = Path.Combine(Path.GetTempPath(), "PromFlow.DiTests", Guid.NewGuid().ToString("N"))
        });
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<LicensePathProvider>();
        services.AddSingleton<FileLicenseStore>();
        services.AddSingleton<ILicenseStore>(sp => sp.GetRequiredService<FileLicenseStore>());
        services.AddSingleton<ILicenseWritableStore>(sp => sp.GetRequiredService<FileLicenseStore>());
        services.AddSingleton<ILicenseRequestExportService, FileLicenseRequestExportService>();
        services.AddSingleton<IInstallationIdentityService, FileInstallationIdentityService>();
        services.AddSingleton<ITrustedTimeStateStore, FileTrustedTimeStateStore>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<LicenseFeatureGate>(provider.GetRequiredService<ILicenseFeatureGate>());
        Assert.IsType<OfflineLicenseVerifier>(provider.GetRequiredService<ILicenseVerifier>());
        Assert.IsType<DefaultLicenseService>(provider.GetRequiredService<ILicenseService>());
        Assert.Same(
            provider.GetRequiredService<ILicenseService>(),
            provider.GetRequiredService<ILicenseStateAccessor>());
        Assert.NotNull(provider.GetRequiredService<ILicenseStore>());
        Assert.NotNull(provider.GetRequiredService<ILicenseWritableStore>());
        Assert.NotNull(provider.GetRequiredService<ILicenseRequestExportService>());
        Assert.NotNull(provider.GetRequiredService<IInstallationIdentityService>());
        Assert.NotNull(provider.GetRequiredService<ITrustedTimeStateStore>());
        Assert.Empty(provider.GetRequiredService<LicensingOptions>().TrustedPublicKeys);
    }

    [Fact]
    public void AddInfrastructure_SourceRegistersLicenseServices()
    {
        var path = Path.Combine(FindRepositoryRoot(), "Configurator.Infrastructure", "DependencyInjection.cs");
        var text = File.ReadAllText(path);

        Assert.Contains("LicensingOptions.SectionName", text);
        Assert.Contains("LicensePathProvider", text);
        Assert.Contains("ILicenseStore", text);
        Assert.Contains("ILicenseWritableStore", text);
        Assert.Contains("ILicenseRequestExportService", text);
        Assert.Contains("FileLicenseStore", text);
        Assert.Contains("FileLicenseRequestExportService", text);
        Assert.Contains("IInstallationIdentityService", text);
        Assert.Contains("FileInstallationIdentityService", text);
        Assert.Contains("ITrustedTimeStateStore", text);
        Assert.Contains("FileTrustedTimeStateStore", text);
    }

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
