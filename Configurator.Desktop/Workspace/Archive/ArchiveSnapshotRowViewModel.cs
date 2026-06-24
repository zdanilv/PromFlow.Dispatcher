using Configurator.Application.Services.Archiving;

namespace Configurator.Desktop.Workspace.Archive;

public sealed class ArchiveSnapshotRowViewModel
{
    public ArchiveSnapshotRowViewModel(RawModbusSnapshotArchiveMetadataRecord record)
    {
        Record = record ?? throw new ArgumentNullException(nameof(record));
        Id = record.Id;
        CapturedAtUtc = record.CapturedAtUtc.ToString("u");
        DeviceId = record.DeviceId;
        Role = record.Role.ToString();
        SequenceNumber = record.SequenceNumber;
        Coils = $"{record.CoilStartAddress} + {record.CoilCount}";
        Registers = $"{record.HoldingRegisterStartAddress} + {record.HoldingRegisterCount}";
        Resolution = record.Resolution.ToString();
        ConfigurationHash = record.ConfigurationHash;
    }

    public RawModbusSnapshotArchiveMetadataRecord Record { get; }

    public Guid Id { get; }

    public string CapturedAtUtc { get; }

    public string DeviceId { get; }

    public string Role { get; }

    public long SequenceNumber { get; }

    public string Coils { get; }

    public string Registers { get; }

    public string Resolution { get; }

    public string ConfigurationHash { get; }
}
