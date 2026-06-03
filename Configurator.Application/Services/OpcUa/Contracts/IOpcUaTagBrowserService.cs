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
/// Описывает сервис просмотра адресного пространства OPC UA сервера и импорта тегов.
/// </summary>
public interface IOpcUaTagBrowserService
{
    /// <summary>
    /// Читает дерево узлов endpoint и возвращает модель, которую UI может показать.
    /// </summary>
    /// <param name="request">Endpoint, стартовый узел и ограничения просмотра адресного пространства.</param>
    /// <param name="cancellationToken">Токен отмены операции просмотра.</param>
    /// <returns>Результат просмотра с деревом узлов или ошибкой подключения/чтения.</returns>
    Task<OpcUaOperationResult<OpcUaBrowseResult>> BrowseAsync(
        OpcUaBrowseRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ищет доступные endpoint-адреса через discovery сервера.
    /// </summary>
    /// <param name="request">Адрес discovery и параметры подключения.</param>
    /// <param name="cancellationToken">Токен отмены discovery-запроса.</param>
    /// <returns>Список найденных endpoint-ов или диагностическая ошибка.</returns>
    Task<OpcUaOperationResult<OpcUaEndpointDiscoveryResult>> DiscoverEndpointsAsync(
        OpcUaEndpointDiscoveryRequest request,
        CancellationToken cancellationToken = default);
}
