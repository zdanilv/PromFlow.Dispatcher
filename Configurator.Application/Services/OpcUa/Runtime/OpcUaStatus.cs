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
/// Агрегирует состояние клиентской и серверной ролей OPC UA для отображения в UI.
/// </summary>
public sealed class OpcUaStatus
{
    /// <summary>
    /// Текущее состояние клиентской роли runtime.
    /// </summary>
    public OpcUaConnectionStatus ClientState { get; init; } = OpcUaConnectionStatus.Disconnected;

    /// <summary>
    /// Текущее состояние серверной роли runtime.
    /// </summary>
    public OpcUaConnectionStatus ServerState { get; init; } = OpcUaConnectionStatus.Disconnected;

    /// <summary>
    /// Последнее диагностическое сообщение клиентской роли.
    /// </summary>
    public string ClientMessage { get; init; } = "Client stopped";

    /// <summary>
    /// Последнее диагностическое сообщение серверной роли.
    /// </summary>
    public string ServerMessage { get; init; } = "Server stopped";

    /// <summary>
    /// Последняя ошибка, зафиксированная runtime.
    /// </summary>
    public string? LastError { get; init; }

    /// <summary>
    /// Время последнего изменения статуса.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>
    /// Начальное состояние runtime до запуска клиента или сервера.
    /// </summary>
    public static OpcUaStatus Stopped { get; } = new();
}
