using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;

namespace Configurator.Application.Services.OpcUa.Browsing;

/// <summary>
/// Описывает запрос просмотра OPC UA endpoint и выбранной ветки адресного пространства.
/// </summary>
public sealed class OpcUaBrowseRequest
{
    /// <summary>
    /// Полный адрес OPC UA endpoint, например opc.tcp://localhost:4840.
    /// </summary>
    public string EndpointUrl { get; set; } = "opc.tcp://localhost:4840";

    /// <summary>
    /// Роль runtime, для которой выполняется импорт тегов.
    /// </summary>
    public OpcUaImportTarget Target { get; set; } = OpcUaImportTarget.ServerTelemetry;

    /// <summary>
    /// Максимальная глубина рекурсивного просмотра адресного пространства.
    /// </summary>
    public int MaxDepth { get; set; } = 8;

    /// <summary>
    /// Максимальное число узлов, которые browser вернет за один запрос.
    /// </summary>
    public int MaxNodes { get; set; } = 2000;

    /// <summary>
    /// Снимок настроек безопасности и идентификации для временной browse-сессии.
    /// </summary>
    public OpcUaOptions Options { get; set; } = new();
}
