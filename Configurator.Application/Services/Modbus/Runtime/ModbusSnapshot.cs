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
    /// <summary>Identity of the transport/datastore session that produced this snapshot.</summary>
    public Guid SourceSessionId { get; init; }

    /// <summary>Acquisition-start order, allocated before any area I/O.</summary>
    public long ReadSequence { get; init; }

    public ModbusAreaReadState? CoilsReadState { get; init; }

    public ModbusAreaReadState? HoldingRegistersReadState { get; init; }

    /// <summary>
    /// Legacy producers without explicit metadata retain their decode contract. Sequence zero
    /// cannot establish a fresh transport read for write confirmation.
    /// </summary>
    public ModbusAreaReadState GetReadState(ModbusDataArea area)
    {
        var state = area == ModbusDataArea.Coil ? CoilsReadState : HoldingRegistersReadState;
        var count = area == ModbusDataArea.Coil ? Coils.Count : HoldingRegisters.Count;
        return state ?? new ModbusAreaReadState(count > 0, count > 0, count > 0,
            count > 0 ? Timestamp : DateTimeOffset.MinValue, 0);
    }

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

/// <summary>Read validity belongs to one area of one snapshot source session.</summary>
public sealed record ModbusAreaReadState(
    bool Enabled,
    bool HasSuccessfulRead,
    bool LastAttemptSucceeded,
    DateTimeOffset LastSuccessfulReadAt,
    long SuccessfulReadSequence)
{
    public bool IsGood => Enabled && HasSuccessfulRead && LastAttemptSucceeded;

    public static ModbusAreaReadState Unavailable(bool enabled) =>
        new(enabled, false, false, DateTimeOffset.MinValue, 0);
}
