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
/// Хранит результат просмотра OPC UA адресного пространства.
/// </summary>
public sealed class OpcUaBrowseResult
{
    /// <summary>
    /// Настройки NodeId и списков тегов для OPC UA ролей.
    /// </summary>
    public IReadOnlyList<OpcUaBrowseNode> Nodes { get; init; } = Array.Empty<OpcUaBrowseNode>();

    /// <summary>
    /// Актуальный агрегированный статус runtime.
    /// </summary>
    public string Status { get; init; } = string.Empty;
}
