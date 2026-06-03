using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Data;

/// <summary>
/// Режим доступа к настроенной точке данных Modbus.
/// </summary>
public enum ModbusDataAccess
{
    /// <summary>
    /// Точку данных можно читать из снимков, но нельзя писать через фасад.
    /// </summary>
    Read,

    /// <summary>
    /// Точку данных можно писать, но от нее не ожидается readable-значение в снимке.
    /// </summary>
    Write,

    /// <summary>
    /// Точку данных можно читать и писать.
    /// </summary>
    ReadWrite
}
