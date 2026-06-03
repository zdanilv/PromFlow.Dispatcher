using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Data;

/// <summary>
/// Последнее типизированное значение настроенной точки данных Modbus.
/// </summary>
/// <param name="Name">Имя настроенной точки данных.</param>
/// <param name="Area">Область Modbus, которая опубликовала значение.</param>
/// <param name="Address">Адрес в выбранной области, начиная с 0.</param>
/// <param name="Length">Количество элементов Modbus, занимаемое значением.</param>
/// <param name="Type">Логический тип значения.</param>
/// <param name="Value">Декодированное значение.</param>
/// <param name="Timestamp">Время создания исходного снимка.</param>
public sealed record ModbusDataValue(
    string Name,
    ModbusDataArea Area,
    int Address,
    int Length,
    ModbusValueType Type,
    object? Value,
    DateTimeOffset Timestamp);
