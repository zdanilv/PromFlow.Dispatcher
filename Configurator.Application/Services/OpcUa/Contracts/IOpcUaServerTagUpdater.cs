using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;

namespace Configurator.Application.Services.OpcUa.Contracts;

/// <summary>
/// Описывает обновление значения тега во встроенном OPC UA сервере.
/// </summary>
public interface IOpcUaServerTagUpdater
{
    /// <summary>
    /// Обновляет значение серверного тега и возвращает результат операции OPC UA.
    /// </summary>
    Task<OpcUaOperationResult> UpdateTagValueAsync(
        OpcUaTagValue value,
        CancellationToken cancellationToken = default);
}
