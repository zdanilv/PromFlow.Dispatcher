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
/// Описывает контракт OPC UA клиента для подключения к endpoint, чтения, записи и подписки на теги.
/// </summary>
public interface IOpcUaClientService
{
    /// <summary>
    /// Подключает OPC UA клиента к endpoint из настроек или переданного запроса.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены операции подключения.</param>
    /// <param name="options">Опциональный снимок настроек; если не передан, реализация использует текущие настройки.</param>
    /// <returns>Результат подключения с ошибкой, если SDK не смог открыть сессию.</returns>
    Task<OpcUaOperationResult> ConnectAsync(
        CancellationToken cancellationToken = default,
        OpcUaOptions? options = null);

    /// <summary>
    /// Закрывает активную OPC UA сессию и очищает ресурсы подключения.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены операции отключения.</param>
    /// <returns>Результат отключения клиента.</returns>
    Task<OpcUaOperationResult> DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Читает текущее значение одного тега по его адресу.
    /// </summary>
    /// <param name="address">Адрес тега, который должен быть прочитан.</param>
    /// <param name="cancellationToken">Токен отмены операции чтения.</param>
    /// <returns>Результат чтения с последним значением тега или диагностикой ошибки.</returns>
    Task<OpcUaOperationResult<OpcUaTagValue>> ReadTagAsync(
        OpcUaTagAddress address,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Подписывается на изменение значения тега и возвращает IDisposable для отмены.
    /// </summary>
    /// <param name="address">Адрес тега, изменения которого нужно отслеживать.</param>
    /// <param name="onValue">Callback, вызываемый при получении нового значения.</param>
    /// <param name="cancellationToken">Токен отмены создания подписки.</param>
    /// <returns>Результат создания подписки; успешное значение нужно освободить через Dispose.</returns>
    Task<OpcUaOperationResult<IDisposable>> SubscribeAsync(
        OpcUaTagAddress address,
        Action<OpcUaTagValue> onValue,
        CancellationToken cancellationToken = default);
}
