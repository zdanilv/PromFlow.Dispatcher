using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Contracts;

/// <summary>
/// Фасад отдельного Modbus TCP стека для экрана ModbusDemo.
/// </summary>
/// <remarks>
/// Интерфейс намеренно повторяет контракт <see cref="IModbusTcpService"/>, но регистрируется
/// как отдельный сервис, чтобы demo-клиент и demo-сервер не делили состояние с основным Modbus экраном.
/// </remarks>
public interface IModbusDemoTcpService : IModbusTcpService;
