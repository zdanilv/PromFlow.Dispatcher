using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;

namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Physical Modbus write attempt produced while executing a semantic command.
/// </summary>
public sealed record PhysicalModbusWriteAuditRecord
{
    public PhysicalModbusWriteAuditRecord(
        Guid writeId,
        Guid? commandId,
        DateTimeOffset attemptedAtUtc,
        DateTimeOffset? completedAtUtc,
        ModbusRuntimeRole role,
        ModbusDataArea area,
        int address,
        int quantity,
        IReadOnlyList<byte> payloadBlob,
        bool succeeded,
        string? errorCode,
        string? errorMessage,
        int schemaVersion)
    {
        ArchiveContractGuards.NotEmpty(writeId, nameof(writeId));
        if (commandId == Guid.Empty)
        {
            throw new ArgumentException("Command identifier must not be empty.", nameof(commandId));
        }

        ArchiveContractGuards.EnumDefined(role, nameof(role));
        ArchiveContractGuards.EnumDefined(area, nameof(area));
        ArchiveContractGuards.NonNegative(address, nameof(address));
        ArchiveContractGuards.Positive(quantity, nameof(quantity));
        ArchiveContractGuards.Positive(schemaVersion, nameof(schemaVersion));

        WriteId = writeId;
        CommandId = commandId;
        AttemptedAtUtc = ArchiveContractGuards.Utc(attemptedAtUtc);
        CompletedAtUtc = ArchiveContractGuards.Utc(completedAtUtc);
        Role = role;
        Area = area;
        Address = address;
        Quantity = quantity;
        PayloadBlob = ArchiveContractGuards.ReadOnlyCopy(payloadBlob, nameof(payloadBlob));
        Succeeded = succeeded;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        SchemaVersion = schemaVersion;
    }

    public Guid WriteId { get; }

    public Guid? CommandId { get; }

    public DateTimeOffset AttemptedAtUtc { get; }

    public DateTimeOffset? CompletedAtUtc { get; }

    public ModbusRuntimeRole Role { get; }

    public ModbusDataArea Area { get; }

    public int Address { get; }

    public int Quantity { get; }

    public IReadOnlyList<byte> PayloadBlob { get; }

    public bool Succeeded { get; }

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public int SchemaVersion { get; }
}
