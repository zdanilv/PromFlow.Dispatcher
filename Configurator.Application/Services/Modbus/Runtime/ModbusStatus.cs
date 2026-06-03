using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Runtime;

/// <summary>
/// Текущее состояние клиента и сервера Modbus.
/// </summary>
public sealed class ModbusStatus
{
    /// <summary>
    /// Состояние клиента.
    /// </summary>
    public ModbusConnectionState ClientState { get; init; } = ModbusConnectionState.Stopped;

    /// <summary>
    /// Состояние сервера.
    /// </summary>
    public ModbusConnectionState ServerState { get; init; } = ModbusConnectionState.Stopped;

    /// <summary>
    /// Последнее сообщение клиента.
    /// </summary>
    public string ClientMessage { get; init; } = "Клиент остановлен";

    /// <summary>
    /// Последнее сообщение сервера.
    /// </summary>
    public string ServerMessage { get; init; } = "Сервер остановлен";

    /// <summary>
    /// Последняя ошибка любой роли.
    /// </summary>
    public string? LastError { get; init; }

    /// <summary>
    /// Время последнего изменения статуса.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>
    /// Начальный остановленный статус.
    /// </summary>
    public static ModbusStatus Stopped { get; } = new();
}
