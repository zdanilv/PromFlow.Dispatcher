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
/// Определяет, в какую роль runtime будут импортированы найденные OPC UA теги.
/// </summary>
public enum OpcUaImportTarget
{
    /// <summary>
    /// Импортированные теги будут публиковаться встроенным сервером как telemetry.
    /// </summary>
    ServerTelemetry,

    /// <summary>
    /// Вариант Client Command для подсистемы OPC UA.
    /// </summary>
    ClientCommand,

    /// <summary>
    /// Клиентская и серверная роли работают одновременно.
    /// </summary>
    Both
}
