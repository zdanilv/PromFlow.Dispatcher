using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Runtime;

namespace Configurator.Application.Services.Modbus.Validation;

/// <summary>
/// Проверяет карту пользовательских тревог Modbus.
/// </summary>
public interface IModbusAlarmMapValidator
{
    /// <summary>
    /// Проверяет AlarmMap и адресные диапазоны выбранной runtime-роли.
    /// </summary>
    ModbusOperationResult Validate(ModbusOptions options, ModbusRunMode mode);
}
