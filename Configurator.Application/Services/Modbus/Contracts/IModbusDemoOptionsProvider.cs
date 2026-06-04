using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Contracts;

/// <summary>
/// Предоставляет снимок настроек named-секции <c>ModbusDemo</c>.
/// </summary>
public interface IModbusDemoOptionsProvider
{
    /// <summary>
    /// Копия текущих настроек demo-стека; вызывающий код может безопасно менять ее перед запуском.
    /// </summary>
    ModbusOptions CurrentValue { get; }
}
