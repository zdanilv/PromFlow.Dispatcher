using Configurator.Application.Services.Modbus.Runtime;

namespace Configurator.Application.Services.Modbus.Contracts;

public interface IModbusDataSnapshotSource
{
    ModbusDataSnapshot CurrentSnapshot { get; }

    event EventHandler<ModbusDataSnapshot>? SnapshotChanged;
}
