using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Data;

/// <summary>
/// Области данных Modbus, поддерживаемые высокоуровневым TCP-сервисом.
/// </summary>
public enum ModbusDataArea
{
    /// <summary>
    /// Coil/discrete output значение.
    /// </summary>
    Coil,

    /// <summary>
    /// Значение Holding Register.
    /// </summary>
    HoldingRegister
}
