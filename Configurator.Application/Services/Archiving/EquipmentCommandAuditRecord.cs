using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Signals;

namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Terminal result of a semantic equipment command.
/// </summary>
public enum EquipmentCommandAuditResult
{
    Requested,
    Succeeded,
    Failed,
    Cancelled
}

/// <summary>
/// Readback confirmation status for a semantic command.
/// </summary>
public enum CommandConfirmationStatus
{
    NotApplicable,
    Pending,
    Confirmed,
    TimedOut,
    ConnectionLost,
    Rejected
}

/// <summary>
/// Operator command intent and terminal outcome.
/// </summary>
public sealed record EquipmentCommandAuditRecord
{
    public EquipmentCommandAuditRecord(
        Guid commandId,
        Guid correlationId,
        DateTimeOffset requestedAtUtc,
        DateTimeOffset? completedAtUtc,
        string? sessionId,
        string? userId,
        string? username,
        string deviceId,
        string signalId,
        SignalValueType valueType,
        string requestedValueCanonical,
        ModbusWriteMode writeMode,
        EquipmentCommandAuditResult result,
        string? errorCode,
        string? errorMessage,
        CommandConfirmationStatus confirmationStatus,
        DateTimeOffset? confirmedAtUtc,
        int schemaVersion)
    {
        ArchiveContractGuards.NotEmpty(commandId, nameof(commandId));
        ArchiveContractGuards.NotEmpty(correlationId, nameof(correlationId));
        ArchiveContractGuards.EnumDefined(valueType, nameof(valueType));
        ArchiveContractGuards.EnumDefined(writeMode, nameof(writeMode));
        ArchiveContractGuards.EnumDefined(result, nameof(result));
        ArchiveContractGuards.EnumDefined(confirmationStatus, nameof(confirmationStatus));
        ArchiveContractGuards.Positive(schemaVersion, nameof(schemaVersion));

        CommandId = commandId;
        CorrelationId = correlationId;
        RequestedAtUtc = ArchiveContractGuards.Utc(requestedAtUtc);
        CompletedAtUtc = ArchiveContractGuards.Utc(completedAtUtc);
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
        Result = result;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ConfirmationStatus = confirmationStatus;
        ConfirmedAtUtc = ArchiveContractGuards.Utc(confirmedAtUtc);
        SchemaVersion = schemaVersion;
    }

    public Guid CommandId { get; }

    public Guid CorrelationId { get; }

    public DateTimeOffset RequestedAtUtc { get; }

    public DateTimeOffset? CompletedAtUtc { get; }

    public string? SessionId { get; }

    public string? UserId { get; }

    public string? Username { get; }

    public string DeviceId { get; }

    public string SignalId { get; }

    public SignalValueType ValueType { get; }

    public string RequestedValueCanonical { get; }

    public ModbusWriteMode WriteMode { get; }

    public EquipmentCommandAuditResult Result { get; }

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public CommandConfirmationStatus ConfirmationStatus { get; }

    public DateTimeOffset? ConfirmedAtUtc { get; }

    public int SchemaVersion { get; }
}
