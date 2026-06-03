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
/// Определяет, какая роль OPC UA runtime опубликовала состояние или snapshot.
/// </summary>
public enum OpcUaRuntimeRole
{
    /// <summary>
    /// Вариант Runtime для подсистемы OPC UA.
    /// </summary>
    Runtime,

    /// <summary>
    /// Запускается или публикует данные клиентская роль.
    /// </summary>
    Client,

    /// <summary>
    /// Запускается или публикует данные серверная роль.
    /// </summary>
    Server
}
