using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Runtime;

/// <summary>
/// Снимок Coils и Holding Registers одной роли Modbus.
/// </summary>
public sealed class ModbusSnapshot
{
    /// <summary>
    /// Роль, которая опубликовала снимок.
    /// </summary>
    public ModbusRuntimeRole Role { get; init; } = ModbusRuntimeRole.Runtime;

    /// <summary>
    /// Значения Coils с адреса 0.
    /// </summary>
    public IReadOnlyList<bool> Coils { get; init; } = Array.Empty<bool>();

    /// <summary>
    /// Значения Holding Registers с адреса 0.
    /// </summary>
    public IReadOnlyList<ushort> HoldingRegisters { get; init; } = Array.Empty<ushort>();

    /// <summary>
    /// Типизированная расшифровка регистров.
    /// </summary>
    public ModbusDecodedRegisters DecodedRegisters { get; init; } = ModbusDecodedRegisters.Empty;

    /// <summary>
    /// Время создания снимка.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    /// <summary>
    /// Пустой снимок.
    /// </summary>
    public static ModbusSnapshot Empty { get; } = new();
}
