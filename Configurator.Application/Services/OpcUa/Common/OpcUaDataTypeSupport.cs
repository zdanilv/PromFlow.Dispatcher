using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;
using System.Globalization;

namespace Configurator.Application.Services.OpcUa.Common;

/// <summary>
/// Преобразует строки настроек в CLR-значения, поддерживаемые OPC UA тегами.
/// </summary>
public static class OpcUaDataTypeSupport
{
    private static readonly HashSet<string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Boolean",
        "SByte",
        "Byte",
        "Int16",
        "UInt16",
        "Int32",
        "UInt32",
        "Int64",
        "UInt64",
        "Float",
        "Double",
        "String"
    };

    /// <summary>
    /// Список CLR-типов, которые текущий runtime умеет читать, писать и показывать в UI.
    /// </summary>
    public static IReadOnlyList<string> SupportedDataTypes { get; } =
    [
        "Boolean",
        "SByte",
        "Byte",
        "Int16",
        "UInt16",
        "Int32",
        "UInt32",
        "Int64",
        "UInt64",
        "Float",
        "Double",
        "String"
    ];

    /// <summary>
    /// Проверяет, поддерживается ли указанный тип данных текущей реализацией OPC UA runtime.
    /// </summary>
    public static bool IsSupported(string? dataType)
        => !string.IsNullOrWhiteSpace(dataType) && SupportedTypes.Contains(dataType);

    /// <summary>
    /// Возвращает строковое значение по умолчанию, которое безопасно парсится для указанного типа.
    /// </summary>
    public static string DefaultInitialValue(string? dataType)
    {
        if (string.Equals(dataType, "Boolean", StringComparison.OrdinalIgnoreCase))
        {
            return "false";
        }

        if (IsInteger(dataType) || IsFloatingPoint(dataType))
        {
            return "0";
        }

        return string.Empty;
    }

    /// <summary>
    /// Преобразует текст из настроек или редактора тегов в CLR-значение для OPC UA SDK.
    /// </summary>
    public static bool TryParse(string? dataType, string text, out object? value, out string expected)
    {
        var type = dataType ?? string.Empty;
        var input = text.Trim();

        if (string.Equals(type, "Boolean", StringComparison.OrdinalIgnoreCase))
        {
            if (bool.TryParse(input, out var boolValue))
            {
                value = boolValue;
                expected = string.Empty;
                return true;
            }

            if (input is "1" or "on" or "ON")
            {
                value = true;
                expected = string.Empty;
                return true;
            }

            if (input is "0" or "off" or "OFF")
            {
                value = false;
                expected = string.Empty;
                return true;
            }

            value = null;
            expected = "true/false";
            return false;
        }

        if (string.Equals(type, "SByte", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseNumber<sbyte>(sbyte.TryParse, input, "SByte", out value, out expected);
        }

        if (string.Equals(type, "Byte", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseNumber<byte>(byte.TryParse, input, "Byte", out value, out expected);
        }

        if (string.Equals(type, "Int16", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseNumber<short>(short.TryParse, input, "Int16", out value, out expected);
        }

        if (string.Equals(type, "UInt16", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseNumber<ushort>(ushort.TryParse, input, "UInt16", out value, out expected);
        }

        if (string.Equals(type, "Int32", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseNumber<int>(int.TryParse, input, "Int32", out value, out expected);
        }

        if (string.Equals(type, "UInt32", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseNumber<uint>(uint.TryParse, input, "UInt32", out value, out expected);
        }

        if (string.Equals(type, "Int64", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseNumber<long>(long.TryParse, input, "Int64", out value, out expected);
        }

        if (string.Equals(type, "UInt64", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseNumber<ulong>(ulong.TryParse, input, "UInt64", out value, out expected);
        }

        if (string.Equals(type, "Float", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
            {
                value = floatValue;
                expected = string.Empty;
                return true;
            }

            value = null;
            expected = "Float";
            return false;
        }

        if (string.Equals(type, "Double", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
            {
                value = doubleValue;
                expected = string.Empty;
                return true;
            }

            value = null;
            expected = "Double";
            return false;
        }

        value = text;
        expected = string.Empty;
        return true;
    }

    /// <summary>
    /// Определяет имя OPC UA типа по CLR-значению, полученному из SDK или UI.
    /// </summary>
    public static string FromClrValue(object? value)
        => value switch
        {
            bool => "Boolean",
            sbyte => "SByte",
            byte => "Byte",
            short => "Int16",
            ushort => "UInt16",
            int => "Int32",
            uint => "UInt32",
            long => "Int64",
            ulong => "UInt64",
            float => "Float",
            double => "Double",
            string => "String",
            _ => "Object"
        };

    private static bool IsInteger(string? dataType)
        => dataType is not null
           && (dataType.Equals("SByte", StringComparison.OrdinalIgnoreCase)
               || dataType.Equals("Byte", StringComparison.OrdinalIgnoreCase)
               || dataType.Equals("Int16", StringComparison.OrdinalIgnoreCase)
               || dataType.Equals("UInt16", StringComparison.OrdinalIgnoreCase)
               || dataType.Equals("Int32", StringComparison.OrdinalIgnoreCase)
               || dataType.Equals("UInt32", StringComparison.OrdinalIgnoreCase)
               || dataType.Equals("Int64", StringComparison.OrdinalIgnoreCase)
               || dataType.Equals("UInt64", StringComparison.OrdinalIgnoreCase));

    private static bool IsFloatingPoint(string? dataType)
        => dataType is not null
           && (dataType.Equals("Float", StringComparison.OrdinalIgnoreCase)
               || dataType.Equals("Double", StringComparison.OrdinalIgnoreCase));

    private delegate bool TryParseDelegate<T>(
        string text,
        NumberStyles styles,
        IFormatProvider? provider,
        out T value);

    private static bool TryParseNumber<T>(
        TryParseDelegate<T> parser,
        string input,
        string typeName,
        out object? value,
        out string expected)
    {
        if (parser(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            expected = string.Empty;
            return true;
        }

        value = null;
        expected = typeName;
        return false;
    }
}
