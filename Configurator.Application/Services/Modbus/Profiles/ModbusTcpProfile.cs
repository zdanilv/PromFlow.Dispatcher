using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Runtime;

namespace Configurator.Application.Services.Modbus.Profiles;

/// <summary>
/// Переносимый снимок всех пользовательских настроек Modbus TCP.
/// </summary>
public sealed class ModbusTcpProfile
{
    public const string CurrentFormat = "PromFlow.ModbusTcpProfile";
    public const int CurrentVersion = 1;

    public string Format { get; set; } = CurrentFormat;

    public int Version { get; set; } = CurrentVersion;

    public ModbusTcpRuntimeSettings Runtime { get; set; } = new();

    public List<ModbusDataPointOptions> DataMap { get; set; } = [];

    public List<ModbusAlarmOptions> AlarmMap { get; set; } = [];

    /// <summary>
    /// Переносимые диагностические имена адресов и битов.
    /// </summary>
    public List<ModbusAddressLabelOptions> AddressLabels { get; set; } = [];

    public static ModbusTcpProfile Create(ModbusOptions runtimeOptions, ModbusOptions mapOptions)
        => new()
        {
            Runtime = ModbusTcpRuntimeSettings.FromOptions(runtimeOptions),
            DataMap = mapOptions.DataMap.Select(point => point.Clone()).ToList(),
            AlarmMap = mapOptions.AlarmMap.Select(alarm => alarm.Clone()).ToList(),
            AddressLabels = (mapOptions.AddressLabels ?? []).Select(label => label.Clone()).ToList()
        };

    public ModbusTcpProfile Clone()
        => new()
        {
            Format = Format,
            Version = Version,
            Runtime = Runtime.Clone(),
            DataMap = DataMap.Select(point => point.Clone()).ToList(),
            AlarmMap = AlarmMap.Select(alarm => alarm.Clone()).ToList(),
            AddressLabels = (AddressLabels ?? []).Select(label => label.Clone()).ToList()
        };
}

/// <summary>
/// Runtime-часть профиля. Legacy <c>ModbusDemo.DataMap</c> намеренно не переносится.
/// </summary>
public sealed class ModbusTcpRuntimeSettings
{
    public bool AutostartOnWorkspaceOpen { get; set; }

    public ModbusRunMode StartupMode { get; set; } = ModbusRunMode.None;

    public ModbusEndpointOptions Client { get; set; } = new();

    public ModbusEndpointOptions Server { get; set; } = new();

    public int WriteConfirmationTimeoutMs { get; set; } = 2000;

    public static ModbusTcpRuntimeSettings FromOptions(ModbusOptions options)
        => new()
        {
            AutostartOnWorkspaceOpen = options.AutostartOnWorkspaceOpen,
            StartupMode = options.StartupMode,
            Client = options.Client.Clone(),
            Server = options.Server.Clone(),
            WriteConfirmationTimeoutMs = options.WriteConfirmationTimeoutMs
        };

    public ModbusOptions ApplyTo(ModbusOptions legacyOptions)
    {
        var result = legacyOptions.Clone();
        result.AutostartOnWorkspaceOpen = AutostartOnWorkspaceOpen;
        result.StartupMode = StartupMode;
        result.Client = Client.Clone();
        result.Server = Server.Clone();
        result.WriteConfirmationTimeoutMs = WriteConfirmationTimeoutMs;
        return result;
    }

    public ModbusTcpRuntimeSettings Clone()
        => new()
        {
            AutostartOnWorkspaceOpen = AutostartOnWorkspaceOpen,
            StartupMode = StartupMode,
            Client = Client.Clone(),
            Server = Server.Clone(),
            WriteConfirmationTimeoutMs = WriteConfirmationTimeoutMs
        };
}
