using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Contracts;

/// <summary>
/// Объединяет управление клиентом и сервером Modbus.
/// </summary>
public interface IModbusRuntimeService : IAsyncDisposable
{
    /// <summary>
    /// Общий статус клиента и сервера.
    /// </summary>
    ModbusStatus Status { get; }

    /// <summary>
    /// Последний снимок клиента.
    /// </summary>
    ModbusSnapshot ClientSnapshot { get; }

    /// <summary>
    /// Последний снимок сервера.
    /// </summary>
    ModbusSnapshot ServerSnapshot { get; }

    /// <summary>
    /// Текущие настройки runtime.
    /// </summary>
    ModbusOptions CurrentOptions { get; }

    /// <summary>
    /// Событие изменения общего статуса.
    /// </summary>
    event EventHandler<ModbusStatus>? StatusChanged;

    /// <summary>
    /// Событие получения снимка от клиента или сервера.
    /// </summary>
    event EventHandler<ModbusSnapshot>? SnapshotChanged;

    /// <summary>
    /// Запускает роли Modbus согласно выбранному режиму.
    /// </summary>
    /// <param name="mode">Какие роли нужно запустить: клиент, сервер или обе.</param>
    /// <param name="options">Опциональный снимок настроек для запуска.</param>
    /// <param name="cancellationToken">Токен отмены lifecycle-команды.</param>
    Task StartAsync(
        ModbusRunMode mode = ModbusRunMode.Both,
        ModbusOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Останавливает обе роли Modbus.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены остановки runtime.</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Перезапускает роли Modbus согласно выбранному режиму.
    /// </summary>
    /// <param name="mode">Какие роли нужно поднять после остановки.</param>
    /// <param name="options">Опциональный снимок настроек для повторного запуска.</param>
    /// <param name="cancellationToken">Токен отмены перезапуска.</param>
    Task RestartAsync(
        ModbusRunMode mode = ModbusRunMode.Both,
        ModbusOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Запускает только клиент.
    /// </summary>
    /// <param name="options">Опциональные настройки клиента.</param>
    /// <param name="cancellationToken">Токен отмены запуска клиента.</param>
    Task StartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Останавливает клиент.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены остановки клиента.</param>
    Task StopClientAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Перезапускает клиент.
    /// </summary>
    /// <param name="options">Опциональные настройки для повторного запуска клиента.</param>
    /// <param name="cancellationToken">Токен отмены перезапуска клиента.</param>
    Task RestartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Запускает только сервер.
    /// </summary>
    /// <param name="options">Опциональные настройки сервера.</param>
    /// <param name="cancellationToken">Токен отмены запуска сервера.</param>
    Task StartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Останавливает сервер.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены остановки сервера.</param>
    Task StopServerAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Перезапускает сервер.
    /// </summary>
    /// <param name="options">Опциональные настройки для повторного запуска сервера.</param>
    /// <param name="cancellationToken">Токен отмены перезапуска сервера.</param>
    Task RestartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default);
}
