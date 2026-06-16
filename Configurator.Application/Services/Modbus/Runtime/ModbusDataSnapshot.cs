using Configurator.Application.Services.Modbus.Data;

namespace Configurator.Application.Services.Modbus.Runtime;

public sealed record ModbusDataSnapshot(
    IReadOnlyDictionary<string, ModbusDataValue> Values,
    DateTimeOffset Timestamp,
    ModbusServiceState State)
{
    public static ModbusDataSnapshot Empty { get; } = new(
        new Dictionary<string, ModbusDataValue>(StringComparer.OrdinalIgnoreCase),
        DateTimeOffset.MinValue,
        ModbusServiceState.Stopped);
}
