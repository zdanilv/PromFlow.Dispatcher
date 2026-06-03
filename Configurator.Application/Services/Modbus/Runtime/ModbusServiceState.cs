using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Runtime;

/// <summary>
/// Агрегированное состояние высокоуровневого сервиса для UI-привязок.
/// </summary>
/// <param name="ActiveRole">Текущая активная роль, которую видит фасад.</param>
/// <param name="ClientState">Текущее состояние жизненного цикла клиента.</param>
/// <param name="ServerState">Текущее состояние жизненного цикла сервера.</param>
/// <param name="IsWaitingForConnection">Запускается ли роль или ожидает переподключения.</param>
/// <param name="Message">Сообщение для отображения текущего состояния.</param>
/// <param name="LastError">Последняя ошибка, полученная от фасада или runtime.</param>
/// <param name="UpdatedAt">Время обновления состояния.</param>
public sealed record ModbusServiceState(
    ModbusRunMode ActiveRole,
    ModbusConnectionState ClientState,
    ModbusConnectionState ServerState,
    bool IsWaitingForConnection,
    string Message,
    string? LastError,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// Начальное остановленное состояние.
    /// </summary>
    public static ModbusServiceState Stopped { get; } = new(
        ModbusRunMode.None,
        ModbusConnectionState.Stopped,
        ModbusConnectionState.Stopped,
        false,
        "Modbus stopped.",
        null,
        DateTimeOffset.Now);
}
