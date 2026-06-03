using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using System.Text;

namespace Configurator.Application.Services.Modbus.Encoding;

/// <summary>
/// Преобразует типизированные значения в Holding Registers и обратно.
/// </summary>
public static class ModbusRegistersCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// Декодирует стандартную карту регистров в типизированные значения.
    /// </summary>
    public static ModbusDecodedRegisters Decode(IReadOnlyList<ushort> registers)
    {
        try
        {
            return new ModbusDecodedRegisters
            {
                Int0 = DecodeInt(registers, 0),
                Int1 = DecodeInt(registers, 1),
                Real23 = DecodeReal(registers, 2),
                String46 = DecodeString(registers, 4, 3),
                Date1011 = DecodeDate(registers, 10),
                Dword1213 = DecodeDword(registers, 12),
                ServerInts1419 = DecodeIntRange(registers, 14, 6)
            };
        }
        catch (Exception ex)
        {
            return new ModbusDecodedRegisters
            {
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Кодирует INT в один регистр.
    /// </summary>
    public static ushort[] EncodeInt(int value)
    {
        return new[] { checked((ushort)value) };
    }

    /// <summary>
    /// Кодирует REAL в два регистра.
    /// </summary>
    public static ushort[] EncodeReal(float value)
    {
        var bytes = BitConverter.GetBytes(value);
        return new[]
        {
            (ushort)(bytes[0] | (bytes[1] << 8)),
            (ushort)(bytes[2] | (bytes[3] << 8))
        };
    }

    /// <summary>
    /// Кодирует STRING в указанное количество регистров.
    /// </summary>
    public static ushort[] EncodeString(string value, int registerCount)
    {
        var capacity = checked(registerCount * 2);
        var bytes = StrictUtf8.GetBytes(value);

        if (bytes.Length > capacity)
        {
            throw new ArgumentException(
                $"Строка занимает {bytes.Length} байт, доступно {capacity}.",
                nameof(value));
        }

        var registers = new ushort[registerCount];

        for (var index = 0; index < registerCount; index++)
        {
            var byteIndex = index * 2;
            var low = byteIndex < bytes.Length ? bytes[byteIndex] : 0;
            var high = byteIndex + 1 < bytes.Length ? bytes[byteIndex + 1] : 0;
            registers[index] = (ushort)(low | (high << 8));
        }

        return registers;
    }

    /// <summary>
    /// Кодирует DATE как Unix timestamp в два регистра.
    /// </summary>
    public static ushort[] EncodeDate(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
            : value.ToUniversalTime();
        var timestamp = (uint)new DateTimeOffset(utc).ToUnixTimeSeconds();
        return EncodeDword(timestamp);
    }

    /// <summary>
    /// Кодирует DWORD в два регистра.
    /// </summary>
    public static ushort[] EncodeDword(uint value)
    {
        return new[]
        {
            (ushort)(value & 0xFFFF),
            (ushort)((value >> 16) & 0xFFFF)
        };
    }

    /// <summary>
    /// Декодирует INT из одного регистра.
    /// </summary>
    public static int DecodeInt(IReadOnlyList<ushort> registers, int address)
    {
        ValidateAddress(registers, address);
        return registers[address];
    }

    /// <summary>
    /// Декодирует REAL из двух регистров.
    /// </summary>
    public static float DecodeReal(IReadOnlyList<ushort> registers, int address)
    {
        ValidateAddress(registers, address);
        ValidateAddress(registers, address + 1);

        var bytes = new byte[4];
        bytes[0] = (byte)(registers[address] & 0xFF);
        bytes[1] = (byte)((registers[address] >> 8) & 0xFF);
        bytes[2] = (byte)(registers[address + 1] & 0xFF);
        bytes[3] = (byte)((registers[address + 1] >> 8) & 0xFF);

        return BitConverter.ToSingle(bytes, 0);
    }

    /// <summary>
    /// Декодирует STRING из нескольких регистров.
    /// </summary>
    public static string DecodeString(IReadOnlyList<ushort> registers, int address, int length)
    {
        ValidateAddress(registers, address);
        ValidateAddress(registers, address + length - 1);

        var bytes = new List<byte>(length * 2);

        for (var index = 0; index < length; index++)
        {
            var register = registers[address + index];
            var low = (byte)(register & 0xFF);
            var high = (byte)((register >> 8) & 0xFF);

            if (low == 0)
            {
                break;
            }

            bytes.Add(low);

            if (high == 0)
            {
                break;
            }

            bytes.Add(high);
        }

        return StrictUtf8.GetString(bytes.ToArray());
    }

    /// <summary>
    /// Декодирует DATE из двух регистров.
    /// </summary>
    public static DateTime DecodeDate(IReadOnlyList<ushort> registers, int address)
    {
        var timestamp = DecodeDword(registers, address);
        return DateTime.UnixEpoch.AddSeconds(timestamp);
    }

    /// <summary>
    /// Декодирует DWORD из двух регистров.
    /// </summary>
    public static uint DecodeDword(IReadOnlyList<ushort> registers, int address)
    {
        ValidateAddress(registers, address);
        ValidateAddress(registers, address + 1);

        return (uint)((registers[address + 1] << 16) | registers[address]);
    }

    private static IReadOnlyList<int> DecodeIntRange(
        IReadOnlyList<ushort> registers,
        int startAddress,
        int count)
    {
        var result = new List<int>();

        for (var index = 0; index < count && startAddress + index < registers.Count; index++)
        {
            result.Add(DecodeInt(registers, startAddress + index));
        }

        return result;
    }

    private static void ValidateAddress(IReadOnlyList<ushort> registers, int address)
    {
        if (address < 0 || address >= registers.Count)
        {
            throw new IndexOutOfRangeException(
                $"Адрес регистра {address} выходит за границы snapshot длиной {registers.Count}.");
        }
    }
}
