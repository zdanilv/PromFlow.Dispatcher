using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Contracts;

/// <summary>
/// Предоставляет настройки секции ModbusDemo, которая управляет общим TCP runtime.
/// </summary>
public interface IModbusDemoOptionsProvider
{
    /// <summary>
    /// Текущие настройки секции ModbusDemo.
    /// </summary>
    ModbusOptions CurrentValue { get; }
}
