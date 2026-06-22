using Configurator.Application.Services.Archiving;
using Configurator.Infrastructure.Persistence.Common;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Common;

public sealed class DefaultAppDataPathProviderTests
{
    [Fact]
    public void DefaultAppDataPathProvider_UsesExplicitArchiveBaseDirectory()
    {
        var explicitBaseDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "archive");
        var options = new ArchiveOptions { BaseDirectory = explicitBaseDirectory };
        var provider = new DefaultAppDataPathProvider();

        var baseDirectory = provider.GetArchiveBaseDirectory(options);
        var exportDirectory = provider.GetArchiveExportDirectory(options);

        Assert.Equal(Path.GetFullPath(explicitBaseDirectory), baseDirectory);
        Assert.Equal(Path.Combine(Path.GetFullPath(explicitBaseDirectory), "Exports"), exportDirectory);
    }

    [Fact]
    public void DefaultAppDataPathProvider_UsesLocalApplicationDataDefault()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var expectedRoot = string.IsNullOrWhiteSpace(localApplicationData)
            ? Path.GetTempPath()
            : localApplicationData;
        var expectedBaseDirectory = Path.Combine(expectedRoot, "PromFlow.Dispatcher", "Archive");
        var provider = new DefaultAppDataPathProvider();

        var baseDirectory = provider.GetArchiveBaseDirectory(new ArchiveOptions());
        var exportDirectory = provider.GetArchiveExportDirectory(new ArchiveOptions());

        Assert.Equal(expectedBaseDirectory, baseDirectory);
        Assert.Equal(Path.Combine(expectedBaseDirectory, "Exports"), exportDirectory);
    }
}
