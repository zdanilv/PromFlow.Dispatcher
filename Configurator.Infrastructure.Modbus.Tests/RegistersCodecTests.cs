using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using System.Text;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed class RegistersCodecTests
{
    [Fact]
    public void Decode_ReturnsTypedValues()
    {
        var registers = new ushort[20];
        ModbusRegistersCodec.EncodeInt(10).CopyTo(registers, 0);
        ModbusRegistersCodec.EncodeInt(20).CopyTo(registers, 1);
        ModbusRegistersCodec.EncodeReal(25.5f).CopyTo(registers, 2);
        ModbusRegistersCodec.EncodeString("ABCDEF", 3).CopyTo(registers, 4);
        ModbusRegistersCodec.EncodeDate(DateTime.UnixEpoch.AddSeconds(1_700_000_000)).CopyTo(registers, 10);
        ModbusRegistersCodec.EncodeDword(0x12345678).CopyTo(registers, 12);

        for (var index = 14; index < 20; index++)
        {
            registers[index] = (ushort)(100 + index);
        }

        var decoded = ModbusRegistersCodec.Decode(registers);

        Assert.Null(decoded.Error);
        Assert.Equal(10, decoded.Int0);
        Assert.Equal(20, decoded.Int1);
        Assert.Equal(25.5f, decoded.Real23);
        Assert.Equal("ABCDEF", decoded.String46);
        Assert.Equal(DateTime.UnixEpoch.AddSeconds(1_700_000_000), decoded.Date1011);
        Assert.Equal((uint)0x12345678, decoded.Dword1213);
        Assert.Equal(new[] { 114, 115, 116, 117, 118, 119 }, decoded.ServerInts1419);
    }

    [Fact]
    public void EncodeDecode_RoundTripsSupportedTypes()
    {
        Assert.Equal(42, ModbusRegistersCodec.DecodeInt(ModbusRegistersCodec.EncodeInt(42), 0));
        Assert.Equal(12.75f, ModbusRegistersCodec.DecodeReal(ModbusRegistersCodec.EncodeReal(12.75f), 0));
        Assert.Equal("HELLO", ModbusRegistersCodec.DecodeString(ModbusRegistersCodec.EncodeString("HELLO", 3), 0, 3));

        var date = new DateTime(2026, 5, 24, 12, 30, 0, DateTimeKind.Utc);
        Assert.Equal(date, ModbusRegistersCodec.DecodeDate(ModbusRegistersCodec.EncodeDate(date), 0));
        Assert.Equal((uint)0xDEADBEEF, ModbusRegistersCodec.DecodeDword(ModbusRegistersCodec.EncodeDword(0xDEADBEEF), 0));
    }

    [Fact]
    public void EncodeDecode_StringRoundTripsUtf8Cyrillic()
    {
        var registers = ModbusRegistersCodec.EncodeString("абв", 3);

        Assert.Equal("абв", ModbusRegistersCodec.DecodeString(registers, 0, 3));
    }

    [Fact]
    public void EncodeString_ThrowsWhenUtf8ValueDoesNotFit()
    {
        var error = Assert.Throws<ArgumentException>(
            () => ModbusRegistersCodec.EncodeString("абвг", 3));

        Assert.Contains("Строка занимает 8 байт, доступно 6", error.Message);
    }

    [Fact]
    public void EncodeDecode_StringKeepsAsciiCompatibility()
    {
        var registers = ModbusRegistersCodec.EncodeString("ABCDEF", 3);

        Assert.Equal("ABCDEF", ModbusRegistersCodec.DecodeString(registers, 0, 3));
    }

    [Fact]
    public void DecodeString_ThrowsForInvalidUtf8Bytes()
    {
        Assert.Throws<DecoderFallbackException>(
            () => ModbusRegistersCodec.DecodeString(new ushort[] { 0x0080 }, 0, 1));
    }

    [Fact]
    public void Decode_ReturnsErrorForShortRegisterMap()
    {
        var decoded = ModbusRegistersCodec.Decode(new ushort[1]);

        Assert.NotNull(decoded.Error);
    }

    [Fact]
    public void DecodeDword_ThrowsForInvalidAddress()
    {
        Assert.Throws<IndexOutOfRangeException>(() => ModbusRegistersCodec.DecodeDword(new ushort[1], 0));
    }
}
