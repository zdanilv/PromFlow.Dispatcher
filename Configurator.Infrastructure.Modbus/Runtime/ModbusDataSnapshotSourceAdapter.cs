using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Runtime;

namespace Configurator.Infrastructure.Modbus.Runtime;

internal sealed class ModbusDataSnapshotSourceAdapter(IModbusDataSnapshotSource source)
    : IModbusDataSnapshotSource
{
    public ModbusDataSnapshot CurrentSnapshot => source.CurrentSnapshot;

    public event EventHandler<ModbusDataSnapshot>? SnapshotChanged
    {
        add => source.SnapshotChanged += value;
        remove => source.SnapshotChanged -= value;
    }
}
