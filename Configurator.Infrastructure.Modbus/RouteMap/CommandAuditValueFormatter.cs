using Configurator.Application.Services.Signals;
using System.Globalization;

namespace Configurator.Infrastructure.Modbus.RouteMap;

public static class CommandAuditValueFormatter
{
    public static string Format(object? value, SignalValueType valueType)
    {
        if (value is null)
        {
            return "<null>";
        }

        return valueType switch
        {
            SignalValueType.Bool => Convert.ToBoolean(value, CultureInfo.InvariantCulture)
                ? "true"
                : "false",
            SignalValueType.Int16 => Convert.ToInt16(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            SignalValueType.UInt16 => Convert.ToUInt16(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            SignalValueType.Int32 => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            SignalValueType.Float32 => Convert.ToSingle(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture),
            SignalValueType.String => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<unknown>"
        };
    }
}
