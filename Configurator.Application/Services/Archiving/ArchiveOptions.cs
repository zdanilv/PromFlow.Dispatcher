namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Application-level archive options. Paths are resolved by later infrastructure stages.
/// </summary>
public sealed class ArchiveOptions
{
    public const string SectionName = "Archive";

    public bool Enabled { get; set; }

    public string BaseDirectory { get; set; } = string.Empty;

    public string DeviceId { get; set; } = string.Empty;

    public int HighResolutionRetentionHours { get; set; } = 72;

    public int LongTermRetentionDays { get; set; } = 90;

    public int LongTermSnapshotIntervalMs { get; set; } = 1000;

    public int ChannelCapacity { get; set; } = 10000;

    public int BatchSize { get; set; } = 250;

    public int BatchFlushIntervalMs { get; set; } = 200;

    public int BusyTimeoutMs { get; set; } = 5000;

    public ArchivePartitionMode PartitionMode { get; set; } = ArchivePartitionMode.Monthly;

    public string ExportDirectory { get; set; } = string.Empty;

    public CommandAuditFailureMode CommandAuditFailureMode { get; set; } = CommandAuditFailureMode.FailOpen;

    public int CommandAuditEnqueueTimeoutMs { get; set; } = 100;

    public List<string> EmergencySignalIds { get; set; } = ["system.emergency"];

    public ArchiveOptions Clone()
        => new()
        {
            Enabled = Enabled,
            BaseDirectory = BaseDirectory,
            DeviceId = DeviceId,
            HighResolutionRetentionHours = HighResolutionRetentionHours,
            LongTermRetentionDays = LongTermRetentionDays,
            LongTermSnapshotIntervalMs = LongTermSnapshotIntervalMs,
            ChannelCapacity = ChannelCapacity,
            BatchSize = BatchSize,
            BatchFlushIntervalMs = BatchFlushIntervalMs,
            BusyTimeoutMs = BusyTimeoutMs,
            PartitionMode = PartitionMode,
            ExportDirectory = ExportDirectory,
            CommandAuditFailureMode = CommandAuditFailureMode,
            CommandAuditEnqueueTimeoutMs = CommandAuditEnqueueTimeoutMs,
            EmergencySignalIds = EmergencySignalIds.ToList()
        };
}
