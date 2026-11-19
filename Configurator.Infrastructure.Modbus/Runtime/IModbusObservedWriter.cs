using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;

namespace Configurator.Infrastructure.Modbus.Runtime;

/// <summary>A successful send receipt, ordered under the same I/O gate as acquisitions.</summary>
internal readonly record struct ModbusWriteReceipt(ModbusRuntimeRole Role, Guid SourceSessionId, long Sequence);

internal interface IModbusObservedWriter
{
    Task<ModbusWriteReceipt> WriteObservedAsync(
        ModbusDataArea area, int address, bool coilValue, ushort[] registers,
        bool pulse, CancellationToken cancellationToken);
}
