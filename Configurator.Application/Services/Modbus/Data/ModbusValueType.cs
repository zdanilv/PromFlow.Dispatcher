using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Data;

/// <summary>
/// Логический тип значения, хранящегося в точке данных Modbus.
/// </summary>
public enum ModbusValueType
{
    /// <summary>
    /// Булево значение, хранящееся в Coil.
    /// </summary>
    Bool,

    /// <summary>
    /// Беззнаковое 16-битное число, хранящееся в одном Holding Register.
    /// </summary>
    UInt16,

    /// <summary>
    /// Целое число, закодированное через <see cref="ModbusRegistersCodec.EncodeInt"/>.
    /// </summary>
    Int,

    /// <summary>
    /// Число с плавающей точкой одинарной точности, закодированное через <see cref="ModbusRegistersCodec.EncodeReal"/>.
    /// </summary>
    Real,

    /// <summary>
    /// UTF-8 строка, закодированная в один или несколько Holding Registers.
    /// </summary>
    String,

    /// <summary>
    /// Дата и время, закодированные как Unix timestamp в Holding Registers.
    /// </summary>
    Date,

    /// <summary>
    /// Беззнаковое 32-битное число, закодированное в двух Holding Registers.
    /// </summary>
    Dword
}
