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
/// Описывает итог импорта тегов из OPC UA адресного пространства.
/// </summary>
public sealed class OpcUaTagImportResult
{
    /// <summary>
    /// Теги, выбранные пользователем для добавления в настройки.
    /// </summary>
    public IReadOnlyList<OpcUaConfiguredTag> Tags { get; init; } = Array.Empty<OpcUaConfiguredTag>();

    /// <summary>
    /// Endpoint, из которого были импортированы выбранные теги.
    /// </summary>
    public OpcUaEndpointProfile? SelectedEndpoint { get; init; }

    /// <summary>
    /// Endpoint-адреса, сохраненные после ручного ввода, discovery или импорта тегов.
    /// </summary>
    public IReadOnlyList<OpcUaEndpointProfile> KnownEndpoints { get; init; } = Array.Empty<OpcUaEndpointProfile>();
}
