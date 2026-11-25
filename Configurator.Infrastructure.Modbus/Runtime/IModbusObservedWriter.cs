using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;

namespace Configurator.Infrastructure.Modbus.Runtime;

/// <summary>A successful send receipt, ordered under the same I/O gate as acquisitions.</summary>
internal readonly record struct ModbusWriteReceipt(ModbusRuntimeRole Role, Guid SourceSessionId, long Sequence);

internal readonly record struct ModbusWritePayload(bool Coil, ushort[] Registers, int? BitIndex, bool BitValue);

internal readonly record struct ModbusWriteAddress(ModbusDataArea Area, int Offset, int? BitIndex);

internal interface IModbusObservedWriter
{
    Task<ModbusWriteReceipt> WriteObservedAsync(
        ModbusWriteAddress address, ModbusWriteAddress? feedback, bool priority, bool pulse,
        Action<Guid> validateBeforeRead, Func<ModbusWriteReceipt, bool, ModbusWritePayload> prepare,
        Action<ModbusWritePayload> committed, CancellationToken cancellationToken);
}

internal sealed class ModbusWriteNotSubmittedException(string message, string? errorCode = null) : InvalidOperationException(message)
{
    public string? ErrorCode { get; } = errorCode;
}
