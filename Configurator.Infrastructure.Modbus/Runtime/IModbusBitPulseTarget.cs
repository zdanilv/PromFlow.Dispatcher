using Configurator.Application.Services.Modbus.Configuration;

namespace Configurator.Infrastructure.Modbus.Runtime;

// ACK pulses require a stable physical target, independently of runtime role changes.
internal interface IModbusBitPulseProvider
{
    IModbusBitPulseTarget CaptureBitPulseTarget(ModbusBitAddressOptions address);
}

internal interface IModbusBitPulseTarget
{
    string Description { get; }
    Task WriteAsync(bool value, CancellationToken cancellationToken);
}

internal sealed class ModbusBitPulseTarget(
    string description,
    Func<bool, CancellationToken, Task> write) : IModbusBitPulseTarget
{
    public string Description { get; } = description;
    public Task WriteAsync(bool value, CancellationToken cancellationToken)
        => write(value, cancellationToken);
}

// Only this exception proves that the edge's write was never submitted.
internal sealed class ModbusBitPulseNotSentException(string message, Exception? innerException = null)
    : Exception(message, innerException);
