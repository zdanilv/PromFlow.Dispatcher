using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Contracts;

/// <summary>
/// Управляет Modbus TCP клиентом: подключением, polling и записью значений.
/// </summary>
public interface IModbusClientService : IAsyncDisposable
{
    /// <summary>
    /// Текущее состояние клиента.
    /// </summary>
    ModbusConnectionState State { get; }

    /// <summary>
    /// Последний снимок, прочитанный клиентом.
    /// </summary>
    ModbusSnapshot Snapshot { get; }

    /// <summary>
    /// Событие изменения состояния клиента.
    /// </summary>
    event EventHandler<ModbusStatus>? StatusChanged;

    /// <summary>
    /// Событие получения снимка от клиента.
    /// </summary>
    event EventHandler<ModbusSnapshot>? SnapshotChanged;

    /// <summary>
    /// Запускает клиент с указанными настройками.
    /// </summary>
    /// <param name="options">Сетевые настройки клиента и параметры polling.</param>
    /// <param name="cancellationToken">Токен отмены запуска клиента.</param>
    Task StartAsync(ModbusEndpointOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Останавливает клиент.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены остановки клиента.</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Записывает одно Coil на сервере.
    /// </summary>
    /// <param name="address">Адрес coil в Modbus-карте.</param>
    /// <param name="value">Логическое значение для записи.</param>
    /// <param name="cancellationToken">Токен отмены операции записи.</param>
    Task WriteCoilAsync(int address, bool value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Записывает один Holding Register на сервере.
    /// </summary>
    /// <param name="address">Адрес holding-регистра.</param>
    /// <param name="value">16-битное значение регистра.</param>
    /// <param name="cancellationToken">Токен отмены операции записи.</param>
    Task WriteRegisterAsync(int address, ushort value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Записывает блок Holding Registers на сервере.
    /// </summary>
    /// <param name="startAddress">Адрес первого holding-регистра в блоке.</param>
    /// <param name="values">Значения регистров в порядке записи.</param>
    /// <param name="cancellationToken">Токен отмены операции записи.</param>
    Task WriteRegistersAsync(int startAddress, ushort[] values, CancellationToken cancellationToken = default);
}
