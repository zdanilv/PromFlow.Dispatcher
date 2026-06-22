using Configurator.Application.Services.Archiving;
using Configurator.Infrastructure.Persistence.Archive;
using Configurator.Infrastructure.Persistence.Common;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Archive;

public sealed class ArchivePartitionResolverTests
{
    [Fact]
    public void PartitionResolver_WritablePartition_UsesUtcMonthAndExpectedFilename()
    {
        var baseDirectory = CreateBaseDirectory();
        var resolver = new ArchivePartitionResolver(new FixedAppDataPathProvider(baseDirectory));
        var options = new ArchiveOptions { DeviceId = "device-01" };

        var partition = resolver.GetWritablePartition(
            options,
            new DateTimeOffset(2026, 3, 1, 1, 30, 0, TimeSpan.FromHours(3)));

        Assert.Equal("device-01", partition.DeviceId);
        Assert.Equal("device-01", partition.SanitizedDeviceId);
        Assert.Equal(2026, partition.Year);
        Assert.Equal(2, partition.Month);
        Assert.Equal(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), partition.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), partition.EndExclusiveUtc);
        Assert.Equal(ArchivePartitionAccess.ReadWrite, partition.Access);
        Assert.Equal(Path.Combine(baseDirectory, "promflow-device-01-2026-02.sqlite"), partition.DatabasePath);
    }

    [Fact]
    public void PartitionResolver_QueryRangeAcrossYearBoundary_ReturnsReadOnlyMonthlyPartitions()
    {
        var baseDirectory = CreateBaseDirectory();
        var resolver = new ArchivePartitionResolver(new FixedAppDataPathProvider(baseDirectory));
        var options = new ArchiveOptions { DeviceId = "plant-01" };

        var partitions = resolver.GetPartitionsForRange(
            options,
            new DateTimeOffset(2025, 12, 31, 23, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal([12, 1, 2], partitions.Select(partition => partition.Month).ToArray());
        Assert.Equal([2025, 2026, 2026], partitions.Select(partition => partition.Year).ToArray());
        Assert.All(partitions, partition => Assert.Equal(ArchivePartitionAccess.ReadOnly, partition.Access));
        Assert.Equal("promflow-plant-01-2025-12.sqlite", Path.GetFileName(partitions[0].DatabasePath));
        Assert.Equal("promflow-plant-01-2026-02.sqlite", Path.GetFileName(partitions[2].DatabasePath));
    }

    [Fact]
    public void PartitionResolver_DeviceIdWithUnsafeCharacters_StaysUnderBaseDirectory()
    {
        var baseDirectory = CreateBaseDirectory();
        var resolver = new ArchivePartitionResolver(new FixedAppDataPathProvider(baseDirectory));
        var options = new ArchiveOptions { DeviceId = "device /line?42*" };

        var partition = resolver.GetWritablePartition(
            options,
            new DateTimeOffset(2026, 6, 22, 12, 0, 0, TimeSpan.Zero));

        var normalizedBaseDirectory = EnsureTrailingDirectorySeparator(Path.GetFullPath(baseDirectory));
        Assert.Equal("device__line_42_", partition.SanitizedDeviceId);
        Assert.Equal("promflow-device__line_42_-2026-06.sqlite", Path.GetFileName(partition.DatabasePath));
        Assert.StartsWith(normalizedBaseDirectory, partition.DatabasePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PartitionResolver_UnsupportedPartitionMode_ThrowsArgumentOutOfRangeException()
    {
        var resolver = new ArchivePartitionResolver(new FixedAppDataPathProvider(CreateBaseDirectory()));
        var options = new ArchiveOptions
        {
            DeviceId = "device-01",
            PartitionMode = (ArchivePartitionMode)999
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => resolver.GetWritablePartition(options, DateTimeOffset.UtcNow));
    }

    private static string CreateBaseDirectory()
        => Path.Combine(Path.GetTempPath(), "PromFlow.PartitionTests", Guid.NewGuid().ToString("N"));

    private static string EnsureTrailingDirectorySeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;

    private sealed class FixedAppDataPathProvider(string baseDirectory) : IAppDataPathProvider
    {
        public string GetArchiveBaseDirectory(ArchiveOptions options)
            => Path.GetFullPath(baseDirectory);

        public string GetArchiveExportDirectory(ArchiveOptions options)
            => Path.Combine(Path.GetFullPath(baseDirectory), "Exports");
    }
}
