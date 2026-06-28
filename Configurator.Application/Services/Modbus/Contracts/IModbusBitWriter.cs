using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Runtime;

namespace Configurator.Application.Services.Modbus.Contracts;

/// <summary>
/// Записывает отдельные Modbus-биты без добавления служебных точек в DataMap.
/// </summary>
public interface IModbusBitWriter
{
    /// <summary>
    /// Записывает true, ждет заданную длительность и возвращает бит в false.
    /// </summary>
    Task<ModbusOperationResult> PulseAsync(
        ModbusBitAddressOptions address,
        int pulseDurationMs,
        CancellationToken cancellationToken = default);
}
