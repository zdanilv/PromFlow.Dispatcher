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
/// Координирует клиентскую и серверную роли OPC UA в desktop runtime.
/// </summary>
public interface IOpcUaRuntimeService : IAsyncDisposable
{
    /// <summary>
    /// Агрегированный статус клиента и сервера.
    /// </summary>
    OpcUaStatus Status { get; }

    /// <summary>
    /// Последний snapshot значений, полученный клиентской ролью.
    /// </summary>
    OpcUaSnapshot ClientSnapshot { get; }

    /// <summary>
    /// Последний snapshot значений, опубликованный серверной ролью.
    /// </summary>
    OpcUaSnapshot ServerSnapshot { get; }

    /// <summary>
    /// Текущие настройки, с которыми runtime был запущен.
    /// </summary>
    OpcUaOptions CurrentOptions { get; }

    /// <summary>
    /// Событие изменения статуса любой роли.
    /// </summary>
    event EventHandler<OpcUaStatus>? StatusChanged;

    /// <summary>
    /// Событие обновления snapshot клиента или сервера.
    /// </summary>
    event EventHandler<OpcUaSnapshot>? SnapshotChanged;

    /// <summary>
    /// Запускает роли согласно выбранному режиму.
    /// </summary>
    /// <param name="mode">Какие роли нужно запустить: клиент, сервер или обе.</param>
    /// <param name="options">Опциональный снимок настроек для запуска.</param>
    /// <param name="cancellationToken">Токен отмены lifecycle-команды.</param>
    Task StartAsync(
        OpcUaRunMode mode = OpcUaRunMode.Both,
        OpcUaOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Останавливает клиентскую и серверную роли.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены остановки runtime.</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Останавливает текущие роли и запускает их заново с актуальными настройками.
    /// </summary>
    /// <param name="mode">Какие роли нужно поднять после остановки.</param>
    /// <param name="options">Опциональный снимок настроек для повторного запуска.</param>
    /// <param name="cancellationToken">Токен отмены перезапуска.</param>
    Task RestartAsync(
        OpcUaRunMode mode = OpcUaRunMode.Both,
        OpcUaOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Запускает только клиентскую роль.
    /// </summary>
    /// <param name="options">Опциональные настройки клиента и списка тегов.</param>
    /// <param name="cancellationToken">Токен отмены запуска клиента.</param>
    Task StartClientAsync(OpcUaOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Останавливает только клиентскую роль.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены остановки клиента.</param>
    Task StopClientAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Перезапускает только клиентскую роль.
    /// </summary>
    /// <param name="options">Опциональные настройки для повторного подключения клиента.</param>
    /// <param name="cancellationToken">Токен отмены перезапуска клиента.</param>
    Task RestartClientAsync(OpcUaOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Запускает только серверную роль.
    /// </summary>
    /// <param name="options">Опциональные настройки сервера и его адресного пространства.</param>
    /// <param name="cancellationToken">Токен отмены запуска сервера.</param>
    Task StartServerAsync(OpcUaOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Останавливает только серверную роль.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены остановки сервера.</param>
    Task StopServerAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Перезапускает только серверную роль.
    /// </summary>
    /// <param name="options">Опциональные настройки для повторного запуска сервера.</param>
    /// <param name="cancellationToken">Токен отмены перезапуска сервера.</param>
    Task RestartServerAsync(OpcUaOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Записывает command-тег через OPC UA клиент и публикует результат в snapshot клиента.
    /// </summary>
    /// <param name="request">Адрес command-тега и значение для записи.</param>
    /// <param name="cancellationToken">Токен отмены операции записи.</param>
    /// <returns>Результат записи в клиентский тег.</returns>
    Task<OpcUaOperationResult> WriteClientTagAsync(
        OpcUaTagWriteRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Обновляет тег встроенного сервера и публикует результат в snapshot сервера.
    /// </summary>
    /// <param name="value">Новое значение серверного тега.</param>
    /// <param name="cancellationToken">Токен отмены обновления тега.</param>
    /// <returns>Результат обновления значения во встроенном сервере.</returns>
    Task<OpcUaOperationResult> UpdateServerTagAsync(
        OpcUaTagValue value,
        CancellationToken cancellationToken = default);
}
