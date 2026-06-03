using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Encoding;

/// <summary>
/// Типизированная расшифровка Holding Registers.
/// </summary>
public sealed class ModbusDecodedRegisters
{
    /// <summary>
    /// INT из регистра 0.
    /// </summary>
    public int? Int0 { get; init; }

    /// <summary>
    /// INT из регистра 1.
    /// </summary>
    public int? Int1 { get; init; }

    /// <summary>
    /// REAL из регистров 2..3.
    /// </summary>
    public float? Real23 { get; init; }

    /// <summary>
    /// STRING из регистров 4..6.
    /// </summary>
    public string? String46 { get; init; }

    /// <summary>
    /// DATE из регистров 10..11.
    /// </summary>
    public DateTime? Date1011 { get; init; }

    /// <summary>
    /// DWORD из регистров 12..13.
    /// </summary>
    public uint? Dword1213 { get; init; }

    /// <summary>
    /// INT значения из регистров 14..19.
    /// </summary>
    public IReadOnlyList<int> ServerInts1419 { get; init; } = Array.Empty<int>();

    /// <summary>
    /// Ошибка декодирования, если карты регистров недостаточно.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Пустая расшифровка.
    /// </summary>
    public static ModbusDecodedRegisters Empty { get; } = new();
}
