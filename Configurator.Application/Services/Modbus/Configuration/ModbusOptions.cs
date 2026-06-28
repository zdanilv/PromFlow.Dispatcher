using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Configuration;

/// <summary>
/// Общие настройки Modbus-подсистемы.
/// </summary>
public sealed class ModbusOptions
{
    /// <summary>
    /// Имя основной секции конфигурации.
    /// </summary>
    public const string SectionName = "Modbus";

    /// <summary>
    /// Имя секции конфигурации для экрана Modbus Demo и общего TCP runtime.
    /// </summary>
    public const string DemoSectionName = "ModbusDemo";

    /// <summary>
    /// Запускать ли Modbus при открытии рабочего пространства.
    /// </summary>
    public bool AutostartOnWorkspaceOpen { get; set; }

    /// <summary>
    /// Режим автозапуска: клиент, сервер или обе роли.
    /// </summary>
    public ModbusRunMode StartupMode { get; set; } = ModbusRunMode.None;

    /// <summary>
    /// Настройки Modbus TCP клиента.
    /// </summary>
    public ModbusEndpointOptions Client { get; set; } = new();

    /// <summary>
    /// Настройки Modbus TCP сервера.
    /// </summary>
    public ModbusEndpointOptions Server { get; set; } = new();

    /// <summary>
    /// Именованные точки данных, доступные через высокоуровневый сервис Modbus TCP.
    /// </summary>
    public List<ModbusDataPointOptions> DataMap { get; set; } = [];

    /// <summary>
    /// Настройки пользовательских тревог и подтверждений, читаемых из общего Modbus snapshot.
    /// </summary>
    public List<ModbusAlarmOptions> AlarmMap { get; set; } = [];

    /// <summary>
    /// Таймаут ожидания подтверждения записи через последующее чтение readable-точек.
    /// </summary>
    public int WriteConfirmationTimeoutMs { get; set; } = 2000;

    /// <summary>
    /// Создает независимую копию настроек.
    /// </summary>
    public ModbusOptions Clone()
    {
        return new ModbusOptions
        {
            AutostartOnWorkspaceOpen = AutostartOnWorkspaceOpen,
            StartupMode = StartupMode,
            Client = Client.Clone(),
            Server = Server.Clone(),
            DataMap = DataMap.Select(point => point.Clone()).ToList(),
            AlarmMap = AlarmMap.Select(alarm => alarm.Clone()).ToList(),
            WriteConfirmationTimeoutMs = WriteConfirmationTimeoutMs
        };
    }
}
