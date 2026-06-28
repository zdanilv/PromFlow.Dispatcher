using Configurator.Application.Services.Modbus.Data;

namespace Configurator.Application.Services.Modbus.Configuration;

/// <summary>
/// Адрес одного Modbus-бита: Coil или бит внутри Holding Register.
/// </summary>
public sealed class ModbusBitAddressOptions
{
    /// <summary>
    /// Область Modbus, в которой хранится бит.
    /// </summary>
    public ModbusDataArea Area { get; set; } = ModbusDataArea.Coil;

    /// <summary>
    /// Zero-based offset внутри выбранной области.
    /// </summary>
    public int Address { get; set; }

    /// <summary>
    /// Номер бита 0..15 для Holding Register. Для Coil не используется.
    /// </summary>
    public int? BitIndex { get; set; }

    /// <summary>
    /// Создает независимую копию адреса.
    /// </summary>
    public ModbusBitAddressOptions Clone()
        => new()
        {
            Area = Area,
            Address = Address,
            BitIndex = BitIndex
        };
}
