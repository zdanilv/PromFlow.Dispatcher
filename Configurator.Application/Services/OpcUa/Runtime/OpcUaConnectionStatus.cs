using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;

namespace Configurator.Application.Services.OpcUa.Runtime;

/// <summary>
/// Перечисляет состояния подключения OPC UA клиента или встроенного сервера.
/// </summary>
public enum OpcUaConnectionStatus
{
    /// <summary>
    /// Соединение отсутствует или роль остановлена.
    /// </summary>
    Disconnected,

    /// <summary>
    /// Выполняется подключение к endpoint.
    /// </summary>
    Connecting,

    /// <summary>
    /// Соединение установлено и готово к обмену данными.
    /// </summary>
    Connected,

    /// <summary>
    /// Клиент пытается восстановить потерянное соединение.
    /// </summary>
    Reconnecting,

    /// <summary>
    /// Сервис перешел в ошибочное состояние.
    /// </summary>
    Faulted
}
