using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Runtime;

/// <summary>
/// Режим запуска Modbus runtime.
/// </summary>
public enum ModbusRunMode
{
    /// <summary>
    /// Не запускать ни одну роль.
    /// </summary>
    None,

    /// <summary>
    /// Запустить только клиент.
    /// </summary>
    Client,

    /// <summary>
    /// Запустить только сервер.
    /// </summary>
    Server,

    /// <summary>
    /// Запустить клиент и сервер.
    /// </summary>
    Both
}
