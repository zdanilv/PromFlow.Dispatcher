using Configurator.Application.Services.Modbus.Data;

namespace Configurator.Application.Services.Signals;

public static class SignalModbusTypeCompatibility
{
    public static bool IsCompatible(SignalValueType signalType, ModbusValueType modbusType) =>
        (signalType, modbusType) switch
        {
            (SignalValueType.Bool, ModbusValueType.Bool) => true,
            (SignalValueType.UInt16 or SignalValueType.Word, ModbusValueType.UInt16 or ModbusValueType.Word) => true,
            (SignalValueType.Int16 or SignalValueType.Int32, ModbusValueType.Int) => true,
            (SignalValueType.Dword, ModbusValueType.Dword) => true,
            (SignalValueType.Float32, ModbusValueType.Real) => true,
            (SignalValueType.String, ModbusValueType.String) => true,
            (SignalValueType.Date, ModbusValueType.Date) => true,
            _ => false
        };

    public static bool TryMapFromModbus(ModbusValueType modbusType, out SignalValueType signalType)
    {
        signalType = modbusType switch
        {
            ModbusValueType.Bool => SignalValueType.Bool,
            ModbusValueType.UInt16 => SignalValueType.UInt16,
            ModbusValueType.Word => SignalValueType.Word,
            ModbusValueType.Int => SignalValueType.Int32,
            ModbusValueType.Real => SignalValueType.Float32,
            ModbusValueType.String => SignalValueType.String,
            ModbusValueType.Date => SignalValueType.Date,
            ModbusValueType.Dword => SignalValueType.Dword,
            _ => default
        };

        return modbusType is ModbusValueType.Bool
            or ModbusValueType.UInt16
            or ModbusValueType.Word
            or ModbusValueType.Int
            or ModbusValueType.Real
            or ModbusValueType.String
            or ModbusValueType.Date
            or ModbusValueType.Dword;
    }

    public static ModbusValueType DefaultModbusType(SignalValueType signalType) =>
        signalType switch
        {
            SignalValueType.Bool => ModbusValueType.Bool,
            SignalValueType.UInt16 => ModbusValueType.UInt16,
            SignalValueType.Word => ModbusValueType.Word,
            SignalValueType.Int16 or SignalValueType.Int32 => ModbusValueType.Int,
            SignalValueType.Dword => ModbusValueType.Dword,
            SignalValueType.Float32 => ModbusValueType.Real,
            SignalValueType.String => ModbusValueType.String,
            SignalValueType.Date => ModbusValueType.Date,
            _ => ModbusValueType.UInt16
        };

    public static int DefaultRegisterLength(SignalValueType signalType) =>
        signalType switch
        {
            SignalValueType.Bool => 1,
            SignalValueType.UInt16 or SignalValueType.Word => 1,
            SignalValueType.Int16 or SignalValueType.Int32 => 1,
            SignalValueType.Dword => 2,
            SignalValueType.Float32 => 2,
            SignalValueType.String => 1,
            SignalValueType.Date => 2,
            _ => 1
        };

    public static int DefaultRegisterLength(ModbusValueType modbusType) =>
        modbusType switch
        {
            ModbusValueType.Bool => 1,
            ModbusValueType.UInt16 or ModbusValueType.Word => 1,
            ModbusValueType.Int => 1,
            ModbusValueType.Dword => 2,
            ModbusValueType.Real => 2,
            ModbusValueType.String => 1,
            ModbusValueType.Date => 2,
            _ => 1
        };

    public static int NormalizeRegisterLength(ModbusValueType modbusType, int currentLength) =>
        modbusType == ModbusValueType.String
            ? Math.Max(1, currentLength)
            : DefaultRegisterLength(modbusType);

    public static IReadOnlyList<ModbusValueType> CompatibleModbusTypes(SignalValueType signalType) =>
        signalType switch
        {
            SignalValueType.Bool => [ModbusValueType.Bool],
            SignalValueType.UInt16 => [ModbusValueType.UInt16, ModbusValueType.Word],
            SignalValueType.Word => [ModbusValueType.Word, ModbusValueType.UInt16],
            SignalValueType.Int16 or SignalValueType.Int32 => [ModbusValueType.Int],
            SignalValueType.Dword => [ModbusValueType.Dword],
            SignalValueType.Float32 => [ModbusValueType.Real],
            SignalValueType.String => [ModbusValueType.String],
            SignalValueType.Date => [ModbusValueType.Date],
            _ => []
        };

    public static SignalValueType NormalizeEquivalent(SignalValueType signalType) =>
        signalType == SignalValueType.Word ? SignalValueType.UInt16 : signalType;
}
