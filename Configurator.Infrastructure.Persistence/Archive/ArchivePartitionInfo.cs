namespace Configurator.Infrastructure.Persistence.Archive;

public sealed record ArchivePartitionInfo(
    string DeviceId,
    string SanitizedDeviceId,
    int Year,
    int Month,
    DateTimeOffset StartUtc,
    DateTimeOffset EndExclusiveUtc,
    string DatabasePath,
    ArchivePartitionAccess Access);
