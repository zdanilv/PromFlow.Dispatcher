using Configurator.Application.Services.Modbus.Data;

namespace Configurator.Application.Services.Modbus.Runtime;

public sealed record ModbusDataSnapshot(
    IReadOnlyDictionary<string, ModbusDataValue> Values,
    DateTimeOffset Timestamp,
    ModbusServiceState State)
{
    /// <summary>Process-local commit order, including resets. Zero is reserved for legacy producers.</summary>
    public long PublicationSequence { get; init; }

    public ModbusRuntimeRole SourceRole { get; init; } = ModbusRuntimeRole.Runtime;

    public Guid SourceSessionId { get; init; }

    public ModbusAreaReadState? CoilsReadState { get; init; }

    public ModbusAreaReadState? HoldingRegistersReadState { get; init; }

    public static ModbusDataSnapshot Empty { get; } = new(
        new Dictionary<string, ModbusDataValue>(StringComparer.OrdinalIgnoreCase),
        DateTimeOffset.MinValue,
        ModbusServiceState.Stopped);
}
