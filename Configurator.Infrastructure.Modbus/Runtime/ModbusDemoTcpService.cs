using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Infrastructure.Modbus.Runtime;

/// <summary>
/// Делегирующий фасад demo-карты поверх общего Modbus runtime.
/// </summary>
internal sealed class ModbusDemoTcpService(
    IModbusTcpService facade) : IModbusDemoTcpService
{
    private int _disposed;

/// <summary>
/// Текущее состояние общего Modbus runtime со стороны demo-фасада.
/// </summary>
    public ModbusServiceState State => facade.State;

    /// <summary>
    /// Уведомляет подписчиков об изменении состояния демо-сервиса.
    /// </summary>
    public event EventHandler<ModbusServiceState>? StateChanged
    {
        add => facade.StateChanged += value;
        remove => facade.StateChanged -= value;
    }

    /// <summary>
    /// Запускает только клиентскую роль runtime.
    /// </summary>
    public Task<ModbusOperationResult> StartClientAsync(ModbusOptions? options = null, CancellationToken ct = default)
        => facade.StartClientAsync(options, ct);

    /// <summary>
    /// Запускает только серверную роль runtime.
    /// </summary>
    public Task<ModbusOperationResult> StartServerAsync(ModbusOptions? options = null, CancellationToken ct = default)
        => facade.StartServerAsync(options, ct);

    /// <summary>
    /// Останавливает активные роли runtime и освобождает сетевые ресурсы.
    /// </summary>
    public Task<ModbusOperationResult> StopAsync(CancellationToken ct = default)
        => facade.StopAsync(ct);

    /// <summary>
    /// Читает значение именованной демо-точки через общий Modbus TCP facade.
    /// </summary>
    public Task<ModbusOperationResult<T>> GetAsync<T>(string name, CancellationToken ct = default)
        => facade.GetAsync<T>(name, ct);

    /// <summary>
    /// Записывает значение именованной демо-точки через общий Modbus TCP facade.
    /// </summary>
    public Task<ModbusOperationResult> SetAsync<T>(string name, T value, CancellationToken ct = default)
        => facade.SetAsync(name, value, ct);

    /// <summary>
    /// Подписывается на изменения demo-точки через общий Modbus TCP facade.
    /// </summary>
    public IDisposable Subscribe(string name, Action<ModbusDataValue> onChanged)
        => facade.Subscribe(name, onChanged);

    /// <summary>
    /// Отписывается от событий, останавливает runtime и освобождает синхронизационные ресурсы.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await facade.DisposeAsync();
    }
}
