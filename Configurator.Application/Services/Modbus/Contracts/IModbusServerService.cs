using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Contracts;

/// <summary>
/// Управляет встроенным Modbus TCP сервером и его локальной картой данных.
/// </summary>
public interface IModbusServerService : IAsyncDisposable
{
    /// <summary>
    /// Текущее состояние сервера.
    /// </summary>
    ModbusConnectionState State { get; }

    /// <summary>
    /// Последний снимок локальной карты данных сервера.
    /// </summary>
    ModbusSnapshot Snapshot { get; }

    /// <summary>
    /// Событие изменения состояния сервера.
    /// </summary>
    event EventHandler<ModbusStatus>? StatusChanged;

    /// <summary>
    /// Событие обновления локальной карты данных сервера.
    /// </summary>
    event EventHandler<ModbusSnapshot>? SnapshotChanged;

    /// <summary>
    /// Запускает сервер с указанными настройками.
    /// </summary>
    /// <param name="options">Сетевые настройки сервера и исходная карта данных.</param>
    /// <param name="cancellationToken">Токен отмены запуска сервера.</param>
    Task StartAsync(ModbusEndpointOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Останавливает сервер.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены остановки сервера.</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Устанавливает значение Coil в локальной карте сервера.
    /// </summary>
    /// <param name="address">Адрес coil в локальной карте.</param>
    /// <param name="value">Логическое значение для публикации.</param>
    /// <param name="cancellationToken">Токен отмены обновления значения.</param>
    Task SetCoilAsync(int address, bool value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Устанавливает значение Holding Register в локальной карте сервера.
    /// </summary>
    /// <param name="address">Адрес holding-регистра.</param>
    /// <param name="value">16-битное значение регистра.</param>
    /// <param name="cancellationToken">Токен отмены обновления значения.</param>
    Task SetRegisterAsync(int address, ushort value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Устанавливает несколько Holding Registers в локальной карте сервера.
    /// </summary>
    /// <param name="startAddress">Адрес первого регистра в блоке.</param>
    /// <param name="values">Значения регистров в порядке публикации.</param>
    /// <param name="cancellationToken">Токен отмены обновления значений.</param>
    Task SetRegistersAsync(int startAddress, ushort[] values, CancellationToken cancellationToken = default);
}
