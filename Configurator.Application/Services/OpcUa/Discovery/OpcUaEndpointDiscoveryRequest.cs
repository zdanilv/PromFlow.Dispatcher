using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;

namespace Configurator.Application.Services.OpcUa.Discovery;

/// <summary>
/// Описывает параметры поиска endpoint-ов OPC UA сервера.
/// </summary>
public sealed class OpcUaEndpointDiscoveryRequest
{
    /// <summary>
    /// Полный адрес OPC UA endpoint, например opc.tcp://localhost:4840.
    /// </summary>
    public string EndpointUrl { get; set; } = "opc.tcp://localhost:4840";

    /// <summary>
    /// Снимок настроек безопасности и идентификации для discovery-запроса.
    /// </summary>
    public OpcUaOptions Options { get; set; } = new();
}
