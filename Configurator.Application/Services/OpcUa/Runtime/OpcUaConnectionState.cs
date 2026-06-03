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
/// Описывает текущее состояние OPC UA подключения и сообщение для диагностики.
/// </summary>
public sealed record OpcUaConnectionState(
    OpcUaConnectionStatus Status,
    string? EndpointUrl,
    string? Message,
    DateTimeOffset Timestamp)
{
    /// <summary>
    /// Создает состояние подключения с текущим timestamp.
    /// </summary>
    public static OpcUaConnectionState Create(
        OpcUaConnectionStatus status,
        string? endpointUrl = null,
        string? message = null)
        => new(status, endpointUrl, message, DateTimeOffset.Now);
}
