using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Runtime;

namespace Configurator.Application.Services.Modbus.Contracts;

public interface IModbusDataMapRuntime
{
    ModbusOperationResult ApplyDataMap(IReadOnlyList<ModbusDataPointOptions> dataMap);

    Task<T> RunDataMapChangeAsync<T>(Func<Task<T>> change, CancellationToken cancellationToken = default) =>
        change();

    /// <summary>
    /// Persists the removal and evicts the point while the shared Modbus roles are stopped.
    /// </summary>
    Task<ModbusOperationResult> RemovePointWhileStoppedAsync(
        string name,
        Func<CancellationToken, Task<bool>> persist,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ModbusOperationResult.Failure("ModbusDataMapRemovalUnavailable",
            "Удаление точки Modbus недоступно."));
}
