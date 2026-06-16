using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Configuration;

/// <summary>
/// Именованная запись высокоуровневой карты данных Modbus.
/// </summary>
public sealed class ModbusDataPointOptions
{
    /// <summary>
    /// Уникальное имя точки данных для вызовов get, set и subscribe.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Область Modbus, в которой хранится значение.
    /// </summary>
    public ModbusDataArea Area { get; set; } = ModbusDataArea.HoldingRegister;

    /// <summary>
    /// Адрес внутри выбранной области Modbus, начиная с 0.
    /// </summary>
    public int Address { get; set; }

    /// <summary>
    /// Количество Coils или Holding Registers, занимаемое значением.
    /// </summary>
    public int Length { get; set; } = 1;

    /// <summary>
    /// Права чтения/записи, которые проверяет фасад.
    /// </summary>
    public ModbusDataAccess Access { get; set; } = ModbusDataAccess.Read;

    /// <summary>
    /// Логический тип значения для кодирования и декодирования.
    /// </summary>
    public ModbusValueType Type { get; set; } = ModbusValueType.UInt16;

    /// <summary>
    /// Номер бита 0..15 для Bool, упакованного в Holding Register.
    /// </summary>
    public int? BitIndex { get; set; }

    /// <summary>
    /// Семантика записи команды: удерживаемое значение или импульс.
    /// </summary>
    public ModbusWriteMode WriteMode { get; set; } = ModbusWriteMode.Latched;

    /// <summary>
    /// Длительность импульса для <see cref="ModbusWriteMode.Pulse"/>.
    /// </summary>
    public int PulseDurationMs { get; set; } = 300;

    /// <summary>
    /// Показывает, можно ли читать точку данных из snapshots.
    /// </summary>
    public bool IsReadable => Access is ModbusDataAccess.Read or ModbusDataAccess.ReadWrite;

    /// <summary>
    /// Показывает, можно ли писать точку данных через фасад.
    /// </summary>
    public bool IsWritable => Access is ModbusDataAccess.Write or ModbusDataAccess.ReadWrite;

    /// <summary>
    /// Создает независимую копию настроек точки данных.
    /// </summary>
    public ModbusDataPointOptions Clone()
        => new()
        {
            Name = Name,
            Area = Area,
            Address = Address,
            Length = Length,
            Access = Access,
            Type = Type,
            BitIndex = BitIndex,
            WriteMode = WriteMode,
            PulseDurationMs = PulseDurationMs
        };
}
