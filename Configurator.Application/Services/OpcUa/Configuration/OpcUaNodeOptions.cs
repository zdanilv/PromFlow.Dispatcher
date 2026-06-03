using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;

namespace Configurator.Application.Services.OpcUa.Configuration;

/// <summary>
/// Хранит идентификаторы NodeId и списки тегов для клиентской и серверной ролей OPC UA.
/// </summary>
public sealed class OpcUaNodeOptions
{
    /// <summary>
    /// Namespace URI, в котором встроенный сервер создает демонстрационные узлы.
    /// </summary>
    public string NamespaceUri { get; set; } = "urn:Configurator:OpcUa:Demo";

    /// <summary>
    /// Показывает, нужно ли брать теги из настроек вместо встроенного demo-набора.
    /// </summary>
    public bool UseConfiguredTags { get; set; }

    /// <summary>
    /// Базовый идентификатор объекта устройства в адресном пространстве сервера.
    /// </summary>
    public string DeviceIdentifier { get; set; } = "DemoDevice";

    /// <summary>
    /// Идентификатор папки, под которой сервер публикует telemetry-теги.
    /// </summary>
    public string TelemetryFolderIdentifier { get; set; } = "DemoDevice/Telemetry";

    /// <summary>
    /// Идентификатор папки, под которой сервер принимает command-теги.
    /// </summary>
    public string CommandsFolderIdentifier { get; set; } = "DemoDevice/Commands";

    /// <summary>
    /// Идентификатор demo-тега, показывающего, что устройство работает.
    /// </summary>
    public string IsRunningIdentifier { get; set; } = "DemoDevice/Telemetry/IsRunning";

    /// <summary>
    /// Идентификатор demo-тега счетчика telemetry.
    /// </summary>
    public string CounterIdentifier { get; set; } = "DemoDevice/Telemetry/Counter";

    /// <summary>
    /// Идентификатор demo-тега с текстовым сообщением сервера.
    /// </summary>
    public string ServerMessageIdentifier { get; set; } = "DemoDevice/Telemetry/ServerMessage";

    /// <summary>
    /// Идентификатор demo-command для логического значения.
    /// </summary>
    public string RequestedBoolIdentifier { get; set; } = "DemoDevice/Commands/RequestedBool";

    /// <summary>
    /// Идентификатор demo-command для целочисленного значения.
    /// </summary>
    public string RequestedIntIdentifier { get; set; } = "DemoDevice/Commands/RequestedInt";

    /// <summary>
    /// Идентификатор demo-command для строкового значения.
    /// </summary>
    public string RequestedTextIdentifier { get; set; } = "DemoDevice/Commands/RequestedText";

    /// <summary>
    /// Общий список telemetry-тегов для обратной совместимости со старой конфигурацией.
    /// </summary>
    public List<OpcUaConfiguredTag> TelemetryTags { get; set; } = [];

    /// <summary>
    /// Общий список command-тегов для обратной совместимости со старой конфигурацией.
    /// </summary>
    public List<OpcUaConfiguredTag> CommandTags { get; set; } = [];

    /// <summary>
    /// Теги, которые встроенный сервер публикует как telemetry.
    /// </summary>
    public List<OpcUaConfiguredTag> ServerTelemetryTags { get; set; } = [];

    /// <summary>
    /// Теги, которые клиентская роль читает как telemetry.
    /// </summary>
    public List<OpcUaConfiguredTag> ClientTelemetryTags { get; set; } = [];

    /// <summary>
    /// Теги, которые встроенный сервер принимает как command от внешних клиентов.
    /// </summary>
    public List<OpcUaConfiguredTag> ServerCommandTags { get; set; } = [];

    /// <summary>
    /// Теги, которые клиентская роль записывает как command.
    /// </summary>
    public List<OpcUaConfiguredTag> ClientCommandTags { get; set; } = [];

    /// <summary>
    /// Создает адрес demo-тега IsRunning.
    /// </summary>
    public OpcUaTagAddress IsRunningAddress()
        => new(NamespaceUri, IsRunningIdentifier, "Boolean");

    /// <summary>
    /// Создает адрес demo-тега Counter.
    /// </summary>
    public OpcUaTagAddress CounterAddress()
        => new(NamespaceUri, CounterIdentifier, "Int32");

    /// <summary>
    /// Создает адрес demo-тега ServerMessage.
    /// </summary>
    public OpcUaTagAddress ServerMessageAddress()
        => new(NamespaceUri, ServerMessageIdentifier, "String");

    /// <summary>
    /// Создает адрес demo-command RequestedBool.
    /// </summary>
    public OpcUaTagAddress RequestedBoolAddress()
        => new(NamespaceUri, RequestedBoolIdentifier, "Boolean");

    /// <summary>
    /// Создает адрес demo-command RequestedInt.
    /// </summary>
    public OpcUaTagAddress RequestedIntAddress()
        => new(NamespaceUri, RequestedIntIdentifier, "Int32");

    /// <summary>
    /// Создает адрес demo-command RequestedText.
    /// </summary>
    public OpcUaTagAddress RequestedTextAddress()
        => new(NamespaceUri, RequestedTextIdentifier, "String");

    /// <summary>
    /// Возвращает адреса telemetry-тегов серверной роли для старого API.
    /// </summary>
    public IReadOnlyList<OpcUaTagAddress> TelemetryAddresses()
        => ServerTelemetryAddresses();

    /// <summary>
    /// Возвращает адреса command-тегов серверной роли для старого API.
    /// </summary>
    public IReadOnlyList<OpcUaTagAddress> CommandAddresses()
        => ServerCommandAddresses();

    /// <summary>
    /// Возвращает адреса тегов, которые встроенный сервер публикует как telemetry.
    /// </summary>
    public IReadOnlyList<OpcUaTagAddress> ServerTelemetryAddresses()
        => ServerTelemetryDefinitions().Select(tag => tag.Address).ToArray();

    /// <summary>
    /// Возвращает адреса тегов, которые клиентская роль читает как telemetry.
    /// </summary>
    public IReadOnlyList<OpcUaTagAddress> ClientTelemetryAddresses()
        => ClientTelemetryDefinitions().Select(tag => tag.Address).ToArray();

    /// <summary>
    /// Возвращает адреса тегов, которые встроенный сервер принимает как command.
    /// </summary>
    public IReadOnlyList<OpcUaTagAddress> ServerCommandAddresses()
        => ServerCommandDefinitions().Select(tag => tag.Address).ToArray();

    /// <summary>
    /// Возвращает адреса тегов, которые клиентская роль записывает как command.
    /// </summary>
    public IReadOnlyList<OpcUaTagAddress> ClientCommandAddresses()
        => ClientCommandDefinitions().Select(tag => tag.Address).ToArray();

    /// <summary>
    /// Возвращает определения telemetry-тегов серверной роли для старого API.
    /// </summary>
    public IReadOnlyList<OpcUaConfiguredTag> TelemetryDefinitions()
        => ServerTelemetryDefinitions();

    /// <summary>
    /// Возвращает определения command-тегов серверной роли для старого API.
    /// </summary>
    public IReadOnlyList<OpcUaConfiguredTag> CommandDefinitions()
        => ServerCommandDefinitions();

    /// <summary>
    /// Возвращает telemetry-теги сервера: настроенные, fallback-общие или demo-набор.
    /// </summary>
    public IReadOnlyList<OpcUaConfiguredTag> ServerTelemetryDefinitions()
    {
        if (!UseConfiguredTags)
        {
            return DefaultTelemetryTags();
        }

        return ServerTelemetryTags.Count > 0
            ? CloneTags(ServerTelemetryTags)
            : CloneTags(TelemetryTags);
    }

    /// <summary>
    /// Возвращает telemetry-теги клиента: настроенные, fallback-общие или demo-набор.
    /// </summary>
    public IReadOnlyList<OpcUaConfiguredTag> ClientTelemetryDefinitions()
    {
        if (!UseConfiguredTags)
        {
            return DefaultTelemetryTags();
        }

        return ClientTelemetryTags.Count > 0
            ? CloneTags(ClientTelemetryTags)
            : CloneTags(TelemetryTags);
    }

    /// <summary>
    /// Возвращает command-теги сервера: настроенные, fallback-общие или demo-набор.
    /// </summary>
    public IReadOnlyList<OpcUaConfiguredTag> ServerCommandDefinitions()
    {
        if (!UseConfiguredTags)
        {
            return DefaultCommandTags();
        }

        return ServerCommandTags.Count > 0
            ? CloneTags(ServerCommandTags)
            : CloneTags(CommandTags);
    }

    /// <summary>
    /// Возвращает command-теги клиента: настроенные, fallback-общие или demo-набор.
    /// </summary>
    public IReadOnlyList<OpcUaConfiguredTag> ClientCommandDefinitions()
    {
        if (!UseConfiguredTags)
        {
            return DefaultCommandTags();
        }

        return ClientCommandTags.Count > 0
            ? CloneTags(ClientCommandTags)
            : CloneTags(CommandTags);
    }

    /// <summary>
    /// Создает независимую копию объекта, чтобы runtime мог работать со снимком настроек без побочных изменений.
    /// </summary>
    public OpcUaNodeOptions Clone()
    {
        return new OpcUaNodeOptions
        {
            NamespaceUri = NamespaceUri,
            UseConfiguredTags = UseConfiguredTags,
            DeviceIdentifier = DeviceIdentifier,
            TelemetryFolderIdentifier = TelemetryFolderIdentifier,
            CommandsFolderIdentifier = CommandsFolderIdentifier,
            IsRunningIdentifier = IsRunningIdentifier,
            CounterIdentifier = CounterIdentifier,
            ServerMessageIdentifier = ServerMessageIdentifier,
            RequestedBoolIdentifier = RequestedBoolIdentifier,
            RequestedIntIdentifier = RequestedIntIdentifier,
            RequestedTextIdentifier = RequestedTextIdentifier,
            TelemetryTags = CloneTags(TelemetryTags),
            CommandTags = CloneTags(CommandTags),
            ServerTelemetryTags = CloneTags(ServerTelemetryTags),
            ClientTelemetryTags = CloneTags(ClientTelemetryTags),
            ServerCommandTags = CloneTags(ServerCommandTags),
            ClientCommandTags = CloneTags(ClientCommandTags)
        };
    }

    /// <summary>
    /// Создает стандартный набор demo telemetry-тегов.
    /// </summary>
    public List<OpcUaConfiguredTag> DefaultTelemetryTags()
        =>
        [
            new()
            {
                Name = "IsRunning",
                Address = IsRunningAddress(),
                Access = OpcUaTagAccess.Read,
                InitialValue = "true"
            },
            new()
            {
                Name = "Counter",
                Address = CounterAddress(),
                Access = OpcUaTagAccess.Read,
                InitialValue = "0"
            },
            new()
            {
                Name = "ServerMessage",
                Address = ServerMessageAddress(),
                Access = OpcUaTagAccess.Read,
                InitialValue = "Server started"
            }
        ];

    /// <summary>
    /// Создает стандартный набор demo command-тегов.
    /// </summary>
    public List<OpcUaConfiguredTag> DefaultCommandTags()
        =>
        [
            new()
            {
                Name = "RequestedBool",
                Address = RequestedBoolAddress(),
                Access = OpcUaTagAccess.ReadWrite,
                InitialValue = "false"
            },
            new()
            {
                Name = "RequestedInt",
                Address = RequestedIntAddress(),
                Access = OpcUaTagAccess.ReadWrite,
                InitialValue = "0"
            },
            new()
            {
                Name = "RequestedText",
                Address = RequestedTextAddress(),
                Access = OpcUaTagAccess.ReadWrite,
                InitialValue = string.Empty
            }
        ];

    private static List<OpcUaConfiguredTag> CloneTags(IEnumerable<OpcUaConfiguredTag> tags)
        => tags.Select(tag => tag.Clone()).ToList();
}
