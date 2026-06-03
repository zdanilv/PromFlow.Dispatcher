using ModbusRx.Device;

namespace Configurator.Infrastructure.Modbus.Client;

/// <summary>
/// Адаптер низкоуровневого чтения и записи через Modbus master.
/// </summary>
internal sealed class ModbusClientDataReader
{
    private readonly ModbusIpMaster _master;
    private readonly byte _unitId;

    /// <summary>
    /// Создает адаптер для Modbus master.
    /// </summary>
    public ModbusClientDataReader(ModbusIpMaster master, int unitId)
    {
        _master = master ?? throw new ArgumentNullException(nameof(master));
        _unitId = (byte)unitId;
    }

    /// <summary>
    /// Читает Coils с указанного Modbus-адреса.
    /// </summary>
    public Task<bool[]> ReadCoilsAsync(int startAddress, int count)
    {
        if (count == 0)
        {
            return Task.FromResult(Array.Empty<bool>());
        }

        return _master.ReadCoilsAsync(
            slaveAddress: _unitId,
            startAddress: checked((ushort)startAddress),
            numberOfPoints: checked((ushort)count));
    }

    /// <summary>
    /// Читает Holding Registers с указанного Modbus-адреса.
    /// </summary>
    public Task<ushort[]> ReadHoldingRegistersAsync(int startAddress, int count)
    {
        if (count == 0)
        {
            return Task.FromResult(Array.Empty<ushort>());
        }

        return _master.ReadHoldingRegistersAsync(
            slaveAddress: _unitId,
            startAddress: checked((ushort)startAddress),
            numberOfPoints: checked((ushort)count));
    }

    /// <summary>
    /// Записывает одно Coil.
    /// </summary>
    public Task WriteCoilAsync(int address, bool value)
    {
        return _master.WriteSingleCoilAsync(_unitId, (ushort)address, value);
    }

    /// <summary>
    /// Записывает один Holding Register.
    /// </summary>
    public Task WriteRegisterAsync(int address, ushort value)
    {
        return _master.WriteSingleRegisterAsync(_unitId, (ushort)address, value);
    }

    /// <summary>
    /// Записывает блок Holding Registers.
    /// </summary>
    public Task WriteRegistersAsync(int startAddress, ushort[] values)
    {
        return _master.WriteMultipleRegistersAsync(_unitId, (ushort)startAddress, values);
    }
}
