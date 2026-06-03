using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Runtime;

/// <summary>
/// Состояние подключения или жизненного цикла роли Modbus.
/// </summary>
public enum ModbusConnectionState
{
    /// <summary>
    /// Роль остановлена.
    /// </summary>
    Stopped,

    /// <summary>
    /// Роль запускается.
    /// </summary>
    Starting,

    /// <summary>
    /// Роль ожидает восстановления подключения.
    /// </summary>
    Reconnecting,

    /// <summary>
    /// Роль работает.
    /// </summary>
    Running,

    /// <summary>
    /// Роль завершилась с ошибкой.
    /// </summary>
    Faulted
}
