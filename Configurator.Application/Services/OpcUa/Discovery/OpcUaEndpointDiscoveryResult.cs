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
/// Хранит endpoint, найденный через discovery OPC UA сервера.
/// </summary>
public sealed class OpcUaEndpointDiscoveryResult
{
    /// <summary>
    /// Endpoint-профили, найденные через discovery или добавленные из исходного адреса.
    /// </summary>
    public IReadOnlyList<OpcUaEndpointProfile> Endpoints { get; init; } = Array.Empty<OpcUaEndpointProfile>();

    /// <summary>
    /// Актуальный агрегированный статус runtime.
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Предупреждения discovery, которые не блокируют выбор endpoint.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
