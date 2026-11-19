namespace Configurator.Infrastructure.Modbus.Runtime;

/// <summary>
/// Process-local ordering of acquisition starts and configuration/write barriers.
/// Allocate acquisition markers under the producer's I/O gate, before reading.
/// </summary>
internal static class ModbusObservationSequence
{
    private static long _sequence;

    internal static long Next() => Interlocked.Increment(ref _sequence);
}
