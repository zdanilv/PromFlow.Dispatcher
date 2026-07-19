using Configurator.Application.Services.Modbus.Data;

namespace Configurator.Application.Services.Modbus.Configuration;

/// <summary>
/// Диагностическое имя адреса Modbus или отдельного бита Holding Register.
/// </summary>
public sealed class ModbusAddressLabelOptions
{
    /// <summary>
    /// Область Modbus, к которой относится метка.
    /// </summary>
    public ModbusDataArea Area { get; set; }

    /// <summary>
    /// Zero-based offset в выбранной области.
    /// </summary>
    public int Address { get; set; }

    /// <summary>
    /// Номер бита Holding Register; <see langword="null"/> именует Coil или слово целиком.
    /// </summary>
    public int? BitIndex { get; set; }

    /// <summary>
    /// Отображаемое диагностическое имя.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Создает независимую копию метки.
    /// </summary>
    public ModbusAddressLabelOptions Clone() => new()
    {
        Area = Area,
        Address = Address,
        BitIndex = BitIndex,
        DisplayName = DisplayName
    };
}
