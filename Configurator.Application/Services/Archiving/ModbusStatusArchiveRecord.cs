using Configurator.Application.Services.Modbus.Runtime;

namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Meaningful Modbus runtime status transition.
/// </summary>
public sealed record ModbusStatusArchiveRecord
{
    public ModbusStatusArchiveRecord(
        Guid id,
        string deviceId,
        DateTimeOffset occurredAtUtc,
        ModbusConnectionState clientState,
        ModbusConnectionState serverState,
        string clientMessage,
        string serverMessage,
        string? lastError,
        int schemaVersion)
    {
        ArchiveContractGuards.NotEmpty(id, nameof(id));
        ArchiveContractGuards.EnumDefined(clientState, nameof(clientState));
        ArchiveContractGuards.EnumDefined(serverState, nameof(serverState));
        ArchiveContractGuards.Positive(schemaVersion, nameof(schemaVersion));

        Id = id;
        DeviceId = ArchiveContractGuards.NotBlank(deviceId, nameof(deviceId));
        OccurredAtUtc = ArchiveContractGuards.Utc(occurredAtUtc);
        ClientState = clientState;
        ServerState = serverState;
        ClientMessage = clientMessage ?? string.Empty;
        ServerMessage = serverMessage ?? string.Empty;
        LastError = lastError;
        SchemaVersion = schemaVersion;
    }

    public Guid Id { get; }

    public string DeviceId { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    public ModbusConnectionState ClientState { get; }

    public ModbusConnectionState ServerState { get; }

    public string ClientMessage { get; }

    public string ServerMessage { get; }

    public string? LastError { get; }

    public int SchemaVersion { get; }
}
