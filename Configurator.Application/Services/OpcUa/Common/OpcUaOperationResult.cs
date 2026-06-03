using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;

namespace Configurator.Application.Services.OpcUa.Common;

/// <summary>
/// Описывает результат операции OPC UA без возвращаемого значения.
/// </summary>
public sealed record OpcUaOperationResult(bool Succeeded, OpcUaError? Error)
{
    /// <summary>
    /// Создает успешный результат операции без возвращаемого значения.
    /// </summary>
    public static OpcUaOperationResult Success()
        => new(true, null);

    /// <summary>
    /// Создает результат ошибки с кодом, сообщением и техническими деталями.
    /// </summary>
    public static OpcUaOperationResult Failure(string code, string message, string? details = null)
        => new(false, new OpcUaError(code, message, details));
}

/// <summary>
/// Описывает результат операции OPC UA без возвращаемого значения.
/// </summary>
public sealed record OpcUaOperationResult<T>(bool Succeeded, T? Value, OpcUaError? Error)
{
    /// <summary>
    /// Создает успешный результат операции с полезным значением.
    /// </summary>
    public static OpcUaOperationResult<T> Success(T value)
        => new(true, value, null);

    /// <summary>
    /// Создает типизированный результат ошибки без полезного значения.
    /// </summary>
    public static OpcUaOperationResult<T> Failure(string code, string message, string? details = null)
        => new(false, default, new OpcUaError(code, message, details));
}
