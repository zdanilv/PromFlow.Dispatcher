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
/// Хранит корневые настройки OPC UA runtime: клиент, сервер, безопасность и адресное пространство.
/// </summary>
public sealed class OpcUaOptions
{
    /// <summary>
    /// Имя секции конфигурации в appsettings и пользовательских настройках.
    /// </summary>
    public const string SectionName = "OpcUa";

    /// <summary>
    /// Определяет, нужно ли запускать runtime при открытии рабочего пространства.
    /// </summary>
    public bool AutostartOnWorkspaceOpen { get; set; }

    /// <summary>
    /// Роль или набор ролей, которые runtime запускает автоматически.
    /// </summary>
    public OpcUaRunMode StartupMode { get; set; } = OpcUaRunMode.None;

    /// <summary>
    /// Настройки клиентской роли runtime.
    /// </summary>
    public OpcUaClientOptions Client { get; set; } = new();

    /// <summary>
    /// Настройки серверной роли runtime.
    /// </summary>
    public OpcUaServerOptions Server { get; set; } = new();

    /// <summary>
    /// Настройки безопасности и аутентификации OPC UA.
    /// </summary>
    public OpcUaSecurityOptions Security { get; set; } = new();

    /// <summary>
    /// Настройки NodeId и списков тегов для OPC UA ролей.
    /// </summary>
    public OpcUaNodeOptions Nodes { get; set; } = new();

    /// <summary>
    /// Endpoint-адреса, сохраненные после ручного ввода, discovery или импорта тегов.
    /// </summary>
    public List<OpcUaEndpointProfile> KnownEndpoints { get; set; } = [];

    /// <summary>
    /// Создает независимую копию объекта, чтобы runtime мог работать со снимком настроек без побочных изменений.
    /// </summary>
    public OpcUaOptions Clone()
    {
        return new OpcUaOptions
        {
            AutostartOnWorkspaceOpen = AutostartOnWorkspaceOpen,
            StartupMode = StartupMode,
            Client = Client.Clone(),
            Server = Server.Clone(),
            Security = Security.Clone(),
            Nodes = Nodes.Clone(),
            KnownEndpoints = KnownEndpoints.Select(endpoint => endpoint.Clone()).ToList()
        };
    }
}

/// <summary>
/// Описывает сохраненный OPC UA endpoint, который можно повторно выбрать в диалоге импорта тегов.
/// </summary>
public sealed class OpcUaEndpointProfile
{
    /// <summary>
    /// Отображаемое имя элемента в UI и настройках.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Полный адрес OPC UA endpoint, например opc.tcp://localhost:4840.
    /// </summary>
    public string EndpointUrl { get; set; } = string.Empty;

    /// <summary>
    /// Время последнего успешного подключения к endpoint.
    /// </summary>
    public DateTimeOffset? LastConnectedAt { get; set; }

    /// <summary>
    /// Источник записи: ручной ввод, discovery, клиентская или серверная настройка.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Создает независимую копию объекта, чтобы runtime мог работать со снимком настроек без побочных изменений.
    /// </summary>
    public OpcUaEndpointProfile Clone()
        => new()
        {
            Name = Name,
            EndpointUrl = EndpointUrl,
            LastConnectedAt = LastConnectedAt,
            Source = Source
        };
}

/// <summary>
/// Хранит настройки OPC UA клиента: идентификаторы приложения, endpoint, таймауты и подписки.
/// </summary>
public sealed class OpcUaClientOptions
{
    /// <summary>
    /// Разрешает запуск соответствующей роли runtime.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Человекочитаемое имя OPC UA приложения, передаваемое в SDK.
    /// </summary>
    public string ApplicationName { get; set; } = "DekstopTemplate OPC UA Client";

    /// <summary>
    /// Уникальный URI экземпляра OPC UA приложения.
    /// </summary>
    public string ApplicationUri { get; set; } = "urn:localhost:DekstopTemplate:OpcUa:Client";

    /// <summary>
    /// URI продукта, который OPC UA SDK включает в описание приложения.
    /// </summary>
    public string ProductUri { get; set; } = "urn:DekstopTemplate";

    /// <summary>
    /// Полный адрес OPC UA endpoint, например opc.tcp://localhost:4840.
    /// </summary>
    public string EndpointUrl { get; set; } = "opc.tcp://localhost:4840";

    /// <summary>
    /// Таймаут OPC UA сессии в миллисекундах.
    /// </summary>
    public int SessionTimeoutMilliseconds { get; set; } = 60000;

    /// <summary>
    /// Максимальное время ожидания подключения в миллисекундах.
    /// </summary>
    public int ConnectTimeoutMilliseconds { get; set; } = 30000;

    /// <summary>
    /// Пауза между попытками переподключения клиента.
    /// </summary>
    public int ReconnectPeriodMilliseconds { get; set; } = 5000;

    /// <summary>
    /// Интервал публикации OPC UA подписки в миллисекундах.
    /// </summary>
    public int SubscriptionPublishingIntervalMilliseconds { get; set; } = 1000;

    /// <summary>
    /// Интервал фоновой записи command-тегов клиентом.
    /// </summary>
    public int CommandWriteIntervalMilliseconds { get; set; } = 3000;

    /// <summary>
    /// Создает независимую копию объекта, чтобы runtime мог работать со снимком настроек без побочных изменений.
    /// </summary>
    public OpcUaClientOptions Clone()
    {
        return new OpcUaClientOptions
        {
            Enabled = Enabled,
            ApplicationName = ApplicationName,
            ApplicationUri = ApplicationUri,
            ProductUri = ProductUri,
            EndpointUrl = EndpointUrl,
            SessionTimeoutMilliseconds = SessionTimeoutMilliseconds,
            ConnectTimeoutMilliseconds = ConnectTimeoutMilliseconds,
            ReconnectPeriodMilliseconds = ReconnectPeriodMilliseconds,
            SubscriptionPublishingIntervalMilliseconds = SubscriptionPublishingIntervalMilliseconds,
            CommandWriteIntervalMilliseconds = CommandWriteIntervalMilliseconds
        };
    }
}

/// <summary>
/// Хранит настройки встроенного OPC UA сервера и демонстрационной telemetry.
/// </summary>
public sealed class OpcUaServerOptions
{
    /// <summary>
    /// Разрешает запуск соответствующей роли runtime.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Включает фоновую генерацию демонстрационных telemetry-значений сервера.
    /// </summary>
    public bool DemoTelemetryEnabled { get; set; } = true;

    /// <summary>
    /// Человекочитаемое имя OPC UA приложения, передаваемое в SDK.
    /// </summary>
    public string ApplicationName { get; set; } = "DekstopTemplate OPC UA Server";

    /// <summary>
    /// Уникальный URI экземпляра OPC UA приложения.
    /// </summary>
    public string ApplicationUri { get; set; } = "urn:localhost:DekstopTemplate:OpcUa:Server";

    /// <summary>
    /// URI продукта, который OPC UA SDK включает в описание приложения.
    /// </summary>
    public string ProductUri { get; set; } = "urn:DekstopTemplate";

    /// <summary>
    /// Полный адрес OPC UA endpoint, например opc.tcp://localhost:4840.
    /// </summary>
    public string EndpointUrl { get; set; } = "opc.tcp://localhost:4840";

    /// <summary>
    /// Интервал обновления демонстрационной telemetry сервера.
    /// </summary>
    public int TelemetryUpdateIntervalMilliseconds { get; set; } = 1000;

    /// <summary>
    /// Создает независимую копию объекта, чтобы runtime мог работать со снимком настроек без побочных изменений.
    /// </summary>
    public OpcUaServerOptions Clone()
    {
        return new OpcUaServerOptions
        {
            Enabled = Enabled,
            DemoTelemetryEnabled = DemoTelemetryEnabled,
            ApplicationName = ApplicationName,
            ApplicationUri = ApplicationUri,
            ProductUri = ProductUri,
            EndpointUrl = EndpointUrl,
            TelemetryUpdateIntervalMilliseconds = TelemetryUpdateIntervalMilliseconds
        };
    }
}

/// <summary>
/// Описывает SDK-независимые настройки безопасности OPC UA.
/// </summary>
public sealed class OpcUaSecurityOptions
{
    /// <summary>
    /// Текстовый режим, который преобразуется в настройки SDK.
    /// </summary>
    public string Mode { get; set; } = "None";

    /// <summary>
    /// Настройки идентификации клиента при подключении к серверу.
    /// </summary>
    public OpcUaAuthenticationOptions Authentication { get; set; } = new();

    /// <summary>
    /// Настройки сертификатов OPC UA приложения.
    /// </summary>
    public OpcUaCertificateOptions Certificates { get; set; } = new();

    /// <summary>
    /// Создает независимую копию объекта, чтобы runtime мог работать со снимком настроек без побочных изменений.
    /// </summary>
    public OpcUaSecurityOptions Clone()
    {
        return new OpcUaSecurityOptions
        {
            Mode = Mode,
            Authentication = Authentication.Clone(),
            Certificates = Certificates.Clone()
        };
    }
}

/// <summary>
/// Описывает настройки аутентификации клиента при подключении к OPC UA серверу.
/// </summary>
public sealed class OpcUaAuthenticationOptions
{
    /// <summary>
    /// Текстовый режим, который преобразуется в настройки SDK.
    /// </summary>
    public string Mode { get; set; } = "Anonymous";

    /// <summary>
    /// Создает независимую копию объекта, чтобы runtime мог работать со снимком настроек без побочных изменений.
    /// </summary>
    public OpcUaAuthenticationOptions Clone()
    {
        return new OpcUaAuthenticationOptions
        {
            Mode = Mode
        };
    }
}

/// <summary>
/// Описывает использование сертификатов OPC UA приложения.
/// </summary>
public sealed class OpcUaCertificateOptions
{
    /// <summary>
    /// Разрешает запуск соответствующей роли runtime.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Создает независимую копию объекта, чтобы runtime мог работать со снимком настроек без побочных изменений.
    /// </summary>
    public OpcUaCertificateOptions Clone()
    {
        return new OpcUaCertificateOptions
        {
            Enabled = Enabled
        };
    }
}
