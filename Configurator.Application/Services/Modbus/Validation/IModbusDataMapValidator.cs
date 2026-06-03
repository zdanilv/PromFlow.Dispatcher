using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Validation;

/// <summary>
/// Валидирует настройки Modbus перед запуском фасада или записью данных.
/// </summary>
public interface IModbusDataMapValidator
{
    /// <summary>
    /// Проверяет настроенную карту данных относительно выбранного режима runtime.
    /// </summary>
    ModbusOperationResult Validate(ModbusOptions options, ModbusRunMode mode);
}
