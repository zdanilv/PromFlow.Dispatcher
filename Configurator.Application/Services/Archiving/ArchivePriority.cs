namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Priority used by the archive ingestion pipeline.
/// </summary>
public enum ArchivePriority
{
    /// <summary>
    /// Command, security, and other terminal records that must not be silently lost.
    /// </summary>
    Critical,

    /// <summary>
    /// Runtime, configuration, and operational events.
    /// </summary>
    Normal,

    /// <summary>
    /// High-frequency raw telemetry records that may be coalesced by later stages.
    /// </summary>
    Telemetry
}

/// <summary>
/// Logical resolution class for retained snapshot records.
/// </summary>
public enum ArchiveResolution
{
    /// <summary>
    /// Every runtime snapshot that the collector accepts.
    /// </summary>
    HighResolution,

    /// <summary>
    /// Down-sampled snapshot retained for long-term diagnostics.
    /// </summary>
    LongTerm
}

/// <summary>
/// Archive partitioning strategy.
/// </summary>
public enum ArchivePartitionMode
{
    /// <summary>
    /// One writable storage partition per UTC month in later persistence stages.
    /// </summary>
    Monthly
}

/// <summary>
/// Type of payload carried by an archive envelope.
/// </summary>
public enum ArchiveRecordKind
{
    RawModbusSnapshot,
    ModbusStatus,
    EquipmentCommandAudit,
    PhysicalModbusWriteAudit,
    SecurityAudit
}

/// <summary>
/// Sort order requested by archive queries.
/// </summary>
public enum ArchiveSortDirection
{
    Ascending,
    Descending
}
