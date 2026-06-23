using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Signals;

namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Explicit correlation context passed from semantic command handling to physical Modbus writes.
/// </summary>
public sealed record CommandExecutionContext
{
    public CommandExecutionContext(
        Guid commandId,
        Guid correlationId,
        DateTimeOffset requestedAtUtc,
        string? sessionId,
        string? userId,
        string? username,
        string deviceId,
        string signalId,
        SignalValueType valueType,
        string requestedValueCanonical,
        ModbusWriteMode? writeMode,
        bool isEmergency,
        int schemaVersion)
    {
        ArchiveContractGuards.NotEmpty(commandId, nameof(commandId));
        ArchiveContractGuards.NotEmpty(correlationId, nameof(correlationId));
        ArchiveContractGuards.EnumDefined(valueType, nameof(valueType));
        if (writeMode is not null)
        {
            ArchiveContractGuards.EnumDefined(writeMode.Value, nameof(writeMode));
        }

        ArchiveContractGuards.Positive(schemaVersion, nameof(schemaVersion));

        CommandId = commandId;
        CorrelationId = correlationId;
        RequestedAtUtc = ArchiveContractGuards.Utc(requestedAtUtc);
        SessionId = sessionId;
        UserId = userId;
        Username = username;
        DeviceId = ArchiveContractGuards.NotBlank(deviceId, nameof(deviceId));
        SignalId = ArchiveContractGuards.NotBlank(signalId, nameof(signalId));
        ValueType = valueType;
        RequestedValueCanonical = ArchiveContractGuards.NotBlank(
            requestedValueCanonical,
            nameof(requestedValueCanonical));
        WriteMode = writeMode;
        IsEmergency = isEmergency;
        SchemaVersion = schemaVersion;
    }

    public Guid CommandId { get; }

    public Guid CorrelationId { get; }

    public DateTimeOffset RequestedAtUtc { get; }

    public string? SessionId { get; }

    public string? UserId { get; }

    public string? Username { get; }

    public string DeviceId { get; }

    public string SignalId { get; }

    public SignalValueType ValueType { get; }

    public string RequestedValueCanonical { get; }

    public ModbusWriteMode? WriteMode { get; }

    public bool IsEmergency { get; }

    public int SchemaVersion { get; }
}
