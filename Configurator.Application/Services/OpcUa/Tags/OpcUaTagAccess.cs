using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;

namespace Configurator.Application.Services.OpcUa.Tags;

/// <summary>
/// Перечисляет права доступа OPC UA тега для чтения и записи.
/// </summary>
public enum OpcUaTagAccess
{
    /// <summary>
    /// Роль или доступ не выбраны.
    /// </summary>
    None = 0,

    /// <summary>
    /// Вариант Read для подсистемы OPC UA.
    /// </summary>
    Read = 1,

    /// <summary>
    /// Вариант Write для подсистемы OPC UA.
    /// </summary>
    Write = 2,

    /// <summary>
    /// Тег или точка доступны для чтения и записи.
    /// </summary>
    ReadWrite = 3
}
