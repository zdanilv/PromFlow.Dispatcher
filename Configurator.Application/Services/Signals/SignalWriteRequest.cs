using Configurator.Application.Services.Modbus.Configuration;

namespace Configurator.Application.Services.Signals;

public sealed record SignalWriteRequest(
    string SignalId,
    object? Value,
    SignalValueType ValueType)
{
    public ModbusWriteMode? RequiredWriteMode { get; init; }
}
