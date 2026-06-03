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
/// Перечисляет варианты запуска ролей OPC UA runtime.
/// </summary>
public enum OpcUaRunMode
{
    /// <summary>
    /// Роль или доступ не выбраны.
    /// </summary>
    None,

    /// <summary>
    /// Запускается или публикует данные клиентская роль.
    /// </summary>
    Client,

    /// <summary>
    /// Запускается или публикует данные серверная роль.
    /// </summary>
    Server,

    /// <summary>
    /// Клиентская и серверная роли работают одновременно.
    /// </summary>
    Both
}
