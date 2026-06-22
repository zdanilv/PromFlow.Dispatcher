using Configurator.Application.Services.Modbus.Runtime;

namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Raw Modbus coils/registers snapshot ready for archive ingestion.
/// </summary>
public sealed record RawModbusSnapshotArchiveRecord
{
    public RawModbusSnapshotArchiveRecord(
        Guid id,
        string deviceId,
        ModbusRuntimeRole role,
        long sequenceNumber,
        DateTimeOffset capturedAtUtc,
        int coilStartAddress,
        int holdingRegisterStartAddress,
        IReadOnlyList<bool> coils,
        IReadOnlyList<ushort> holdingRegisters,
        string configurationHash,
        ArchiveResolution resolution,
        int schemaVersion)
    {
        ArchiveContractGuards.NotEmpty(id, nameof(id));
        ArchiveContractGuards.EnumDefined(role, nameof(role));
        ArchiveContractGuards.EnumDefined(resolution, nameof(resolution));
        ArchiveContractGuards.NonNegative(sequenceNumber, nameof(sequenceNumber));
        ArchiveContractGuards.NonNegative(coilStartAddress, nameof(coilStartAddress));
        ArchiveContractGuards.NonNegative(holdingRegisterStartAddress, nameof(holdingRegisterStartAddress));
        ArchiveContractGuards.Positive(schemaVersion, nameof(schemaVersion));

        Id = id;
        DeviceId = ArchiveContractGuards.NotBlank(deviceId, nameof(deviceId));
        Role = role;
        SequenceNumber = sequenceNumber;
        CapturedAtUtc = ArchiveContractGuards.Utc(capturedAtUtc);
        CoilStartAddress = coilStartAddress;
        HoldingRegisterStartAddress = holdingRegisterStartAddress;
        Coils = ArchiveContractGuards.ReadOnlyCopy(coils, nameof(coils));
        HoldingRegisters = ArchiveContractGuards.ReadOnlyCopy(holdingRegisters, nameof(holdingRegisters));
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

    public int HoldingRegisterStartAddress { get; }

    public IReadOnlyList<bool> Coils { get; }

    public IReadOnlyList<ushort> HoldingRegisters { get; }

    public string ConfigurationHash { get; }

    public ArchiveResolution Resolution { get; }

    public int SchemaVersion { get; }
}
