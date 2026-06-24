using Configurator.Application.Services.Modbus.Runtime;

namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Raw Modbus snapshot scalar metadata without decoded coil/register payloads.
/// </summary>
public sealed record RawModbusSnapshotArchiveMetadataRecord
{
    public RawModbusSnapshotArchiveMetadataRecord(
        Guid id,
        string deviceId,
        ModbusRuntimeRole role,
        long sequenceNumber,
        DateTimeOffset capturedAtUtc,
        int coilStartAddress,
        int coilCount,
        int holdingRegisterStartAddress,
        int holdingRegisterCount,
        string configurationHash,
        ArchiveResolution resolution,
        int schemaVersion)
    {
        ArchiveContractGuards.NotEmpty(id, nameof(id));
        ArchiveContractGuards.EnumDefined(role, nameof(role));
        ArchiveContractGuards.NonNegative(sequenceNumber, nameof(sequenceNumber));
        ArchiveContractGuards.NonNegative(coilStartAddress, nameof(coilStartAddress));
        ArchiveContractGuards.NonNegative(coilCount, nameof(coilCount));
        ArchiveContractGuards.NonNegative(holdingRegisterStartAddress, nameof(holdingRegisterStartAddress));
        ArchiveContractGuards.NonNegative(holdingRegisterCount, nameof(holdingRegisterCount));
        ArchiveContractGuards.EnumDefined(resolution, nameof(resolution));
        ArchiveContractGuards.Positive(schemaVersion, nameof(schemaVersion));

        Id = id;
        DeviceId = ArchiveContractGuards.NotBlank(deviceId, nameof(deviceId));
        Role = role;
        SequenceNumber = sequenceNumber;
        CapturedAtUtc = ArchiveContractGuards.Utc(capturedAtUtc);
        CoilStartAddress = coilStartAddress;
        CoilCount = coilCount;
        HoldingRegisterStartAddress = holdingRegisterStartAddress;
        HoldingRegisterCount = holdingRegisterCount;
        ConfigurationHash = ArchiveContractGuards.NotBlank(configurationHash, nameof(configurationHash));
        Resolution = resolution;
        SchemaVersion = schemaVersion;
    }

    public Guid Id { get; }

    public string DeviceId { get; }

    public ModbusRuntimeRole Role { get; }

    public long SequenceNumber { get; }

    public DateTimeOffset CapturedAtUtc { get; }

    public int CoilStartAddress { get; }

    public int CoilCount { get; }

    public int HoldingRegisterStartAddress { get; }

    public int HoldingRegisterCount { get; }

    public string ConfigurationHash { get; }

    public ArchiveResolution Resolution { get; }

    public int SchemaVersion { get; }
}
