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
/// Описывает контракт встроенного OPC UA сервера и его lifecycle.
/// </summary>
public interface IOpcUaServerService
{
    /// <summary>
    /// Запускает сервер с конфигурацией из текущих настроек.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены запуска сервера.</param>
    /// <param name="options">Опциональный снимок настроек; если не передан, используется текущая конфигурация.</param>
    /// <returns>Результат запуска встроенного сервера.</returns>
    Task<OpcUaOperationResult> StartAsync(
        CancellationToken cancellationToken = default,
        OpcUaOptions? options = null);

    /// <summary>
    /// Останавливает сервер и освобождает SDK-ресурсы.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены остановки сервера.</param>
    /// <returns>Результат остановки встроенного сервера.</returns>
    Task<OpcUaOperationResult> StopAsync(CancellationToken cancellationToken = default);
}
