using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Runtime;

/// <summary>
/// Источник статуса или снимка в Modbus runtime.
/// </summary>
public enum ModbusRuntimeRole
{
    /// <summary>
    /// Общий runtime.
    /// </summary>
    Runtime,

    /// <summary>
    /// Modbus TCP клиент.
    /// </summary>
    Client,

    /// <summary>
    /// Встроенный Modbus TCP сервер.
    /// </summary>
    Server
}
