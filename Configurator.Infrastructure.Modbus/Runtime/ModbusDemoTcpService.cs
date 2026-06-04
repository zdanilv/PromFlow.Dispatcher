using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Infrastructure.Modbus.Runtime;

/// <summary>
/// Делегирующий фасад, владеющий отдельным Modbus-стеком демо-экрана.
/// </summary>
/// <remarks>
/// Основная логика запуска, чтения, записи и подтверждения остается в <see cref="IModbusTcpService"/>.
/// Этот класс нужен как владелец demo-runtime и точка DisposeAsync для всех его компонентов.
/// </remarks>
internal sealed class ModbusDemoTcpService(
    IModbusTcpService facade,
    IModbusRuntimeService runtime,
    IModbusClientService client,
    IModbusServerService server) : IModbusDemoTcpService
{
    private int _disposed;

    /// <summary>
    /// Текущее состояние независимого demo-фасада Modbus.
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
    /// Запускает клиентскую роль demo-runtime с настройками секции ModbusDemo.
    /// </summary>
    public Task<ModbusOperationResult> StartClientAsync(ModbusOptions? options = null, CancellationToken ct = default)
        => facade.StartClientAsync(options, ct);

    /// <summary>
    /// Запускает серверную роль demo-runtime с настройками секции ModbusDemo.
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
    /// Останавливает demo-runtime и освобождает компоненты, созданные вручную в DI.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await facade.DisposeAsync();
        await runtime.DisposeAsync();
        await client.DisposeAsync();
        await server.DisposeAsync();
    }
}
