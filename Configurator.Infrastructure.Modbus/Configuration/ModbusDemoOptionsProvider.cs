using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Microsoft.Extensions.Options;

namespace Configurator.Infrastructure.Modbus.Configuration;

/// <summary>
/// Читает настройки демо-экрана из named-секции <c>ModbusDemo</c>.
/// </summary>
internal sealed class ModbusDemoOptionsProvider(
    IOptionsMonitor<ModbusOptions> optionsMonitor) : IModbusDemoOptionsProvider
{
    /// <summary>
    /// Возвращает clone, чтобы ViewModel могла открыть диалог настроек без изменения live-options.
    /// </summary>
    public ModbusOptions CurrentValue => optionsMonitor.Get(ModbusOptions.DemoSectionName).Clone();
}
