using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Contracts;

/// <summary>
/// Маркерный фасад для независимого Modbus TCP демо-экрана.
/// </summary>
public interface IModbusDemoTcpService : IModbusTcpService;
