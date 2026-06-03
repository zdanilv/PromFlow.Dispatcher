using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Configuration;

/// <summary>
/// Настройки одной Modbus-роли: клиента или сервера.
/// </summary>
public sealed class ModbusEndpointOptions
{
    /// <summary>
    /// Адрес Modbus TCP сервера, к которому подключается клиент.
    /// </summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>
    /// Адрес интерфейса, на котором запускается сервер.
    /// </summary>
    public string BindAddress { get; set; } = "127.0.0.1";

    /// <summary>
    /// TCP-порт Modbus.
    /// </summary>
    public int Port { get; set; } = 502;

    /// <summary>
    /// UnitId устройства Modbus.
    /// </summary>
    public int UnitId { get; set; } = 1;

    /// <summary>
    /// Интервал опроса данных клиентом или серверной витриной.
    /// </summary>
    public int PollIntervalMs { get; set; } = 500;

    /// <summary>
    /// Включена ли роль при запуске в режиме Both.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Разрешает чтение и запись Coils для клиента.
    /// </summary>
    public bool CoilsEnabled { get; set; } = true;

    /// <summary>
    /// Разрешает чтение и запись Holding Registers для клиента.
    /// </summary>
    public bool HoldingRegistersEnabled { get; set; } = true;

    /// <summary>
    /// Начальный адрес Coils при чтении внешнего устройства.
    /// </summary>
    public int CoilStartAddress { get; set; }

    /// <summary>
    /// Начальный адрес Holding Registers при чтении внешнего устройства.
    /// </summary>
    public int HoldingRegisterStartAddress { get; set; }

    /// <summary>
    /// Количество Coils, отображаемых и читаемых с начального адреса.
    /// </summary>
    public int CoilCount { get; set; } = 10;

    /// <summary>
    /// Количество Holding Registers, отображаемых и читаемых с начального адреса.
    /// </summary>
    public int RegisterCount { get; set; } = 20;

    /// <summary>
    /// Создает копию настроек, чтобы фоновые задачи не зависели от изменений UI.
    /// </summary>
    public ModbusEndpointOptions Clone()
    {
        return new ModbusEndpointOptions
        {
            Host = Host,
            BindAddress = BindAddress,
            Port = Port,
            UnitId = UnitId,
            PollIntervalMs = PollIntervalMs,
            Enabled = Enabled,
            CoilsEnabled = CoilsEnabled,
            HoldingRegistersEnabled = HoldingRegistersEnabled,
            CoilStartAddress = CoilStartAddress,
            HoldingRegisterStartAddress = HoldingRegisterStartAddress,
            CoilCount = CoilCount,
            RegisterCount = RegisterCount
        };
    }
}
