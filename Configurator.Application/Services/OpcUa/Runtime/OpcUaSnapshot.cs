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
/// Фиксирует последние telemetry и command значения одной роли OPC UA runtime.
/// </summary>
public sealed class OpcUaSnapshot
{
    /// <summary>
    /// Роль runtime, к которой относится статус или snapshot.
    /// </summary>
    public OpcUaRuntimeRole Role { get; init; } = OpcUaRuntimeRole.Runtime;

    /// <summary>
    /// Последние telemetry-значения, опубликованные ролью runtime.
    /// </summary>
    public IReadOnlyList<OpcUaTagValue> TelemetryValues { get; init; } = Array.Empty<OpcUaTagValue>();

    /// <summary>
    /// Последние command-значения, записанные ролью runtime.
    /// </summary>
    public IReadOnlyList<OpcUaTagValue> CommandValues { get; init; } = Array.Empty<OpcUaTagValue>();

    /// <summary>
    /// Время получения или публикации значения.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    /// <summary>
    /// Пустое значение, используемое до первого успешного обновления.
    /// </summary>
    public static OpcUaSnapshot Empty { get; } = new();
}
