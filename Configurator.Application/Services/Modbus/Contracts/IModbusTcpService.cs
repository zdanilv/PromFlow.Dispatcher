using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Contracts;

/// <summary>
/// Высокоуровневый DI-фасад для безопасного взаимодействия с Modbus TCP клиентом и сервером.
/// </summary>
public interface IModbusTcpService : IAsyncDisposable
{
    /// <summary>
    /// Текущее состояние фасада, полученное из связанного Modbus runtime.
    /// </summary>
    ModbusServiceState State { get; }

    /// <summary>
    /// Вызывается при каждом изменении состояния фасада.
    /// </summary>
    event EventHandler<ModbusServiceState>? StateChanged;

    /// <summary>
    /// Запускает роль Modbus TCP клиента через фасад.
    /// </summary>
    Task<ModbusOperationResult> StartClientAsync(
        ModbusOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Запускает роль Modbus TCP сервера через фасад.
    /// </summary>
    Task<ModbusOperationResult> StartServerAsync(
        ModbusOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Останавливает все роли Modbus, которыми управляет связанный runtime.
    /// </summary>
    Task<ModbusOperationResult> StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Возвращает последнее кешированное типизированное значение настроенной точки данных.
    /// </summary>
    /// <param name="name">Имя точки из Modbus-карты данных.</param>
    /// <param name="ct">Токен отмены операции чтения.</param>
    /// <typeparam name="T">Ожидаемый CLR-тип значения.</typeparam>
    /// <returns>Результат чтения с типизированным значением или диагностикой ошибки.</returns>
    Task<ModbusOperationResult<T>> GetAsync<T>(
        string name,
        CancellationToken ct = default);

    /// <summary>
    /// Записывает типизированное значение в настроенную точку данных.
    /// </summary>
    /// <param name="name">Имя точки из Modbus-карты данных.</param>
    /// <param name="value">Значение, которое будет закодировано в Modbus-область.</param>
    /// <param name="ct">Токен отмены операции записи.</param>
    /// <typeparam name="T">CLR-тип записываемого значения.</typeparam>
    /// <returns>Результат записи и подтверждения значения.</returns>
    Task<ModbusOperationResult> SetAsync<T>(
        string name,
        T value,
        CancellationToken ct = default);

    /// <summary>
    /// Подписывается на изменения настроенной точки данных и возвращает disposable-подписку.
    /// </summary>
    /// <param name="name">Имя точки из Modbus-карты данных.</param>
    /// <param name="onChanged">Callback, который получает новое значение точки.</param>
    /// <returns>Подписка, которую нужно освободить через Dispose.</returns>
    IDisposable Subscribe(string name, Action<ModbusDataValue> onChanged);
}
