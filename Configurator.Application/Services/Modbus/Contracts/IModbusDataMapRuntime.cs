using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Runtime;

namespace Configurator.Application.Services.Modbus.Contracts;

public interface IModbusDataMapRuntime
{
    ModbusOperationResult ApplyDataMap(IReadOnlyList<ModbusDataPointOptions> dataMap);
}
