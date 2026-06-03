using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Runtime;

/// <summary>
/// Результат высокоуровневой операции Modbus.
/// </summary>
/// <param name="Succeeded">Успешно ли завершилась операция.</param>
/// <param name="ErrorCode">Стабильный машинно-читаемый код ошибки.</param>
/// <param name="ErrorMessage">Сообщение об ошибке для пользователя.</param>
/// <param name="ErrorDetails">Дополнительные диагностические детали.</param>
public sealed record ModbusOperationResult(
    bool Succeeded,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorDetails)
{
    /// <summary>
    /// Создает успешный результат операции.
    /// </summary>
    public static ModbusOperationResult Success()
        => new(true, null, null, null);

    /// <summary>
    /// Создает неуспешный результат операции.
    /// </summary>
    public static ModbusOperationResult Failure(string code, string message, string? details = null)
        => new(false, code, message, details);
}

/// <summary>
/// Результат высокоуровневой операции Modbus с возвращаемым значением.
/// </summary>
/// <typeparam name="T">Тип возвращаемого значения.</typeparam>
/// <param name="Succeeded">Успешно ли завершилась операция.</param>
/// <param name="Value">Возвращаемое значение при успешной операции.</param>
/// <param name="ErrorCode">Стабильный машинно-читаемый код ошибки.</param>
/// <param name="ErrorMessage">Сообщение об ошибке для пользователя.</param>
/// <param name="ErrorDetails">Дополнительные диагностические детали.</param>
public sealed record ModbusOperationResult<T>(
    bool Succeeded,
    T? Value,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorDetails)
{
    /// <summary>
    /// Создает успешный результат операции со значением.
    /// </summary>
    public static ModbusOperationResult<T> Success(T value)
        => new(true, value, null, null, null);

    /// <summary>
    /// Создает неуспешный результат операции.
    /// </summary>
    public static ModbusOperationResult<T> Failure(string code, string message, string? details = null)
        => new(false, default, code, message, details);
}
