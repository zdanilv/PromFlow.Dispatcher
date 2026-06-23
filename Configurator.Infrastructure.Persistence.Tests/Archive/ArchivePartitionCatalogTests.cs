using Configurator.Application.Services.Archiving;
using Configurator.Infrastructure.Persistence.Archive;
using Configurator.Infrastructure.Persistence.Common;
using Configurator.Infrastructure.Persistence.Tests.TestSupport;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Archive;

public sealed class ArchivePartitionCatalogTests
{
    [Fact]
    public void GetExistingPartitions_FiltersByRangeAndDeviceWithoutCreatingFiles()
    {
        using var database = new TempArchiveDatabase();
        File.WriteAllText(Path.Combine(database.DirectoryPath, "promflow-device-1-2026-01.sqlite"), string.Empty);
        File.WriteAllText(Path.Combine(database.DirectoryPath, "promflow-device-1-2026-02.sqlite"), string.Empty);
        File.WriteAllText(Path.Combine(database.DirectoryPath, "promflow-device-2-2026-02.sqlite"), string.Empty);
        File.WriteAllText(Path.Combine(database.DirectoryPath, "notes.txt"), string.Empty);
        var catalog = new ArchivePartitionCatalog(new FixedAppDataPathProvider(database.DirectoryPath));
        var options = new ArchiveOptions { BaseDirectory = database.DirectoryPath, DeviceId = "device-1" };
        var query = new ArchiveQuery(
            fromUtc: new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            toUtc: new DateTimeOffset(2026, 2, 28, 23, 59, 59, TimeSpan.Zero),
            deviceId: "device-1");

        var partitions = catalog.GetExistingPartitions(options, query);

        var partition = Assert.Single(partitions);
        Assert.Equal("device-1", partition.DeviceId);
        Assert.Equal(2026, partition.Year);
        Assert.Equal(2, partition.Month);
        Assert.Equal(ArchivePartitionAccess.ReadOnly, partition.Access);
        Assert.Equal(3, Directory.EnumerateFiles(database.DirectoryPath, "*.sqlite").Count());
    }

    [Fact]
    public void GetCurrentWritablePartition_SanitizesDeviceIdUnderArchiveDirectory()
    {
        using var database = new TempArchiveDatabase();
        var catalog = new ArchivePartitionCatalog(new FixedAppDataPathProvider(database.DirectoryPath));
        var options = new ArchiveOptions
        {
            BaseDirectory = database.DirectoryPath,
            DeviceId = "device /line?42*"
        };

        var partition = catalog.GetCurrentWritablePartition(
            options,
            new DateTimeOffset(2026, 6, 22, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(Path.GetFullPath(database.DirectoryPath), Path.GetDirectoryName(partition.DatabasePath));
        Assert.EndsWith("promflow-device__line_42_-2026-06.sqlite", partition.DatabasePath, StringComparison.Ordinal);
        Assert.Equal(ArchivePartitionAccess.ReadWrite, partition.Access);
    }

    private sealed class FixedAppDataPathProvider(string baseDirectory) : IAppDataPathProvider
    {
        public string GetArchiveBaseDirectory(ArchiveOptions options)
            => Path.GetFullPath(baseDirectory);

        public string GetArchiveExportDirectory(ArchiveOptions options)
            => Path.Combine(Path.GetFullPath(baseDirectory), "Exports");
    }
}
