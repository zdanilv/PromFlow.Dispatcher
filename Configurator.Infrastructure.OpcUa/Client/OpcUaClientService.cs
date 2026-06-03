using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;
using Configurator.Infrastructure.OpcUa.Common;
using Configurator.Infrastructure.OpcUa.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace Configurator.Infrastructure.OpcUa.Client;

/// <summary>
/// Инкапсулирует OPC UA SDK-клиент: подключение, чтение, запись, подписки и восстановление состояния.
/// </summary>
public sealed class OpcUaClientService :
    IOpcUaClientService,
    IOpcUaTagWriter,
    IOpcUaConnectionStateProvider,
    IAsyncDisposable
{
    private readonly OpcUaApplicationConfigurationFactory _configurationFactory;
    private readonly IOpcUaIdentityProvider _identityProvider;
    private readonly ILogger<OpcUaClientService> _logger;
    private readonly IOptionsMonitor<OpcUaOptions> _optionsMonitor;
    private readonly IOpcUaSecurityProvider _securityProvider;
    private readonly ObservableValue<OpcUaConnectionState> _states;
    private readonly ITelemetryContext _telemetry = DefaultTelemetry.Create(_ => { });
    private readonly IOpcUaTagWriteRequestValidator _writeValidator;
    private readonly List<Subscription> _subscriptions = [];
    // SDK-сессия и подписки меняют состояние из UI-команд и callbacks KeepAlive,
    // поэтому доступ к ним синхронизируется через общий объект блокировки.
    private readonly object _sessionLock = new();
    private readonly object _optionsLock = new();
    private OpcUaOptions _options;
    private ISession? _session;

    /// <summary>
    /// Создает клиентский сервис и подготавливает поток состояний подключения.
    /// </summary>
    public OpcUaClientService(
        IOptionsMonitor<OpcUaOptions> optionsMonitor,
        OpcUaApplicationConfigurationFactory configurationFactory,
        IOpcUaSecurityProvider securityProvider,
        IOpcUaIdentityProvider identityProvider,
        IOpcUaTagWriteRequestValidator writeValidator,
        ILogger<OpcUaClientService> logger)
    {
        _optionsMonitor = optionsMonitor;
        _configurationFactory = configurationFactory;
        _securityProvider = securityProvider;
        _identityProvider = identityProvider;
        _writeValidator = writeValidator;
        _logger = logger;
        _options = optionsMonitor.CurrentValue.Clone();
        _states = new ObservableValue<OpcUaConnectionState>(
            OpcUaConnectionState.Create(OpcUaConnectionStatus.Disconnected, _options.Client.EndpointUrl));
    }

    /// <summary>
    /// Текущее состояние OPC UA подключения, опубликованное клиентским сервисом.
    /// </summary>
    public OpcUaConnectionState Current => _states.Current;

    /// <summary>
    /// Наблюдаемый поток изменений состояния OPC UA подключения.
    /// </summary>
    public IObservable<OpcUaConnectionState> ConnectionStates => _states;

    /// <summary>
    /// Открывает соединение с endpoint и подготавливает клиентскую сессию к операциям чтения, записи и подписки.
    /// </summary>
    public async Task<OpcUaOperationResult> ConnectAsync(
        CancellationToken cancellationToken = default,
        OpcUaOptions? options = null)
    {
        var currentOptions = SetOptions(options);
        var security = _securityProvider.GetSecurity(currentOptions);
        var identity = _identityProvider.GetIdentity(currentOptions);

        if (!IsNoneSecurity(security.Mode) || security.CertificatesEnabled)
        {
            return OpcUaOperationResult.Failure(
                "SecurityNotSupported",
                "Only Security.Mode=None and Certificates.Enabled=false are supported in v1.");
        }

        if (!IsAnonymous(identity.Mode))
        {
            return OpcUaOperationResult.Failure(
                "AuthenticationNotSupported",
                "Only Authentication.Mode=Anonymous is supported in v1.");
        }

        lock (_sessionLock)
        {
            if (_session is { Connected: true })
            {
                return OpcUaOperationResult.Success();
            }
        }

        try
        {
            _states.Publish(OpcUaConnectionState.Create(
                OpcUaConnectionStatus.Connecting,
                currentOptions.Client.EndpointUrl,
                "Connecting to OPC UA server."));

            // В v1 endpoint выбирается явно для None/Anonymous, чтобы не зависеть от discovery
            // и не поднимать сертификатную безопасность там, где она отключена в настройках.
            var configuration = _configurationFactory.CreateClient(currentOptions);
            configuration.TransportQuotas.OperationTimeout = Math.Max(1000, currentOptions.Client.ConnectTimeoutMilliseconds);
            var selectedEndpoint = CreateAnonymousEndpointDescription(currentOptions);

            var endpoint = new ConfiguredEndpoint(
                collection: null,
                selectedEndpoint,
                EndpointConfiguration.Create(configuration));

            var sessionFactory = new DefaultSessionFactory(_telemetry);
            var session = await sessionFactory.CreateAsync(
                configuration,
                endpoint,
                updateBeforeConnect: false,
                sessionName: currentOptions.Client.ApplicationName,
                sessionTimeout: (uint)Math.Max(1000, currentOptions.Client.SessionTimeoutMilliseconds),
                identity: null,
                preferredLocales: null,
                cancellationToken);

            session.KeepAlive += OnKeepAlive;
            session.TransferSubscriptionsOnReconnect = true;

            lock (_sessionLock)
            {
                _session = session;
            }

            _states.Publish(OpcUaConnectionState.Create(
                OpcUaConnectionStatus.Connected,
                currentOptions.Client.EndpointUrl,
                "OPC UA client connected."));

            return OpcUaOperationResult.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _states.Publish(OpcUaConnectionState.Create(
                OpcUaConnectionStatus.Faulted,
                currentOptions.Client.EndpointUrl,
                ex.Message));

            _logger.LogWarning(ex, "Failed to connect to OPC UA endpoint {EndpointUrl}", currentOptions.Client.EndpointUrl);
            return ToFailure(ex, "ConnectFailed", "Failed to connect to OPC UA server.");
        }
    }

    /// <summary>
    /// Закрывает клиентскую сессию и переводит соединение в остановленное состояние.
    /// </summary>
    public async Task<OpcUaOperationResult> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ISession? session;
        OpcUaOptions currentOptions;

        lock (_sessionLock)
        {
            session = _session;
            _session = null;
            _subscriptions.Clear();
        }

        lock (_optionsLock)
        {
            currentOptions = _options;
        }

        if (session is null)
        {
            _states.Publish(OpcUaConnectionState.Create(
                OpcUaConnectionStatus.Disconnected,
                currentOptions.Client.EndpointUrl));
            return OpcUaOperationResult.Success();
        }

        try
        {
            session.KeepAlive -= OnKeepAlive;
            await session.CloseAsync(closeChannel: true, cancellationToken);
            session.Dispose();
            _states.Publish(OpcUaConnectionState.Create(
                OpcUaConnectionStatus.Disconnected,
                currentOptions.Client.EndpointUrl));
            return OpcUaOperationResult.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToFailure(ex, "DisconnectFailed", "Failed to close OPC UA session.");
        }
    }

    /// <summary>
    /// Читает значение одного OPC UA тега по его адресу.
    /// </summary>
    public async Task<OpcUaOperationResult<OpcUaTagValue>> ReadTagAsync(
        OpcUaTagAddress address,
        CancellationToken cancellationToken = default)
    {
        var sessionResult = GetConnectedSession();
        if (!sessionResult.Succeeded || sessionResult.Value is null)
        {
            return OpcUaOperationResult<OpcUaTagValue>.Failure(
                sessionResult.Error?.Code ?? "SessionNotConnected",
                sessionResult.Error?.Message ?? "OPC UA session is not connected.",
                sessionResult.Error?.Details);
        }

        if (address.IsEmpty)
        {
            return OpcUaOperationResult<OpcUaTagValue>.Failure(
                "TagAddressEmpty",
                "Tag address must not be empty.");
        }

        try
        {
            var nodeId = ResolveNodeId(sessionResult.Value, address);
            var dataValue = await sessionResult.Value.ReadValueAsync(nodeId, cancellationToken);

            return OpcUaOperationResult<OpcUaTagValue>.Success(
                new OpcUaTagValue(
                    address,
                    dataValue.Value,
                    ToTimestamp(dataValue),
                    dataValue.StatusCode.ToString()));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return OpcUaOperationResult<OpcUaTagValue>.Failure(
                "ReadFailed",
                $"Failed to read tag '{address.Identifier}'.",
                ex.Message);
        }
    }

    /// <summary>
    /// Создает подписку на изменения значения OPC UA тега и возвращает ресурс для ее отмены.
    /// </summary>
    public async Task<OpcUaOperationResult<IDisposable>> SubscribeAsync(
        OpcUaTagAddress address,
        Action<OpcUaTagValue> onValue,
        CancellationToken cancellationToken = default)
    {
        var sessionResult = GetConnectedSession();
        if (!sessionResult.Succeeded || sessionResult.Value is null)
        {
            return OpcUaOperationResult<IDisposable>.Failure(
                sessionResult.Error?.Code ?? "SessionNotConnected",
                sessionResult.Error?.Message ?? "OPC UA session is not connected.",
                sessionResult.Error?.Details);
        }

        if (address.IsEmpty)
        {
            return OpcUaOperationResult<IDisposable>.Failure(
                "TagAddressEmpty",
                "Tag address must not be empty.");
        }

        OpcUaOptions currentOptions;
        lock (_optionsLock)
        {
            currentOptions = _options;
        }

        try
        {
            var nodeId = ResolveNodeId(sessionResult.Value, address);
            var publishingInterval = Math.Max(100, currentOptions.Client.SubscriptionPublishingIntervalMilliseconds);
            // Публикуем состояние до чтения тегов, чтобы UI сразу показывал успешное подключение.
            var subscription = new Subscription(_telemetry, new SubscriptionOptions())
            {
                DisplayName = $"Tag subscription: {address.Identifier}",
                PublishingEnabled = true,
                PublishingInterval = publishingInterval,
                KeepAliveCount = 10,
                LifetimeCount = 100,
                MaxNotificationsPerPublish = 1000,
                Priority = 1
            };

            var item = new MonitoredItem(_telemetry, new MonitoredItemOptions())
            {
                DisplayName = address.Identifier,
                StartNodeId = nodeId,
                AttributeId = Attributes.Value,
                SamplingInterval = publishingInterval,
                QueueSize = 10,
                DiscardOldest = true
            };

            item.Notification += (_, _) =>
            {
                foreach (var value in item.DequeueValues())
                {
                    onValue(new OpcUaTagValue(
                        address,
                        value.Value,
                        ToTimestamp(value),
                        value.StatusCode.ToString()));
                }
            };

            subscription.AddItem(item);
            sessionResult.Value.AddSubscription(subscription);
            await subscription.CreateAsync(cancellationToken);

            lock (_sessionLock)
            {
                _subscriptions.Add(subscription);
            }

            return OpcUaOperationResult<IDisposable>.Success(new DisposableAction(() =>
            {
                lock (_sessionLock)
                {
                    _subscriptions.Remove(subscription);
                }
            }));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return OpcUaOperationResult<IDisposable>.Failure(
                "SubscribeFailed",
                $"Failed to subscribe to tag '{address.Identifier}'.",
                ex.Message);
        }
    }

    /// <summary>
    /// Записывает значение в OPC UA тег с предварительно проверенным адресом и типом данных.
    /// </summary>
    public async Task<OpcUaOperationResult> WriteTagAsync(
        OpcUaTagWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = _writeValidator.Validate(request);
        if (!validation.Succeeded)
        {
            return validation;
        }

        var sessionResult = GetConnectedSession();
        if (!sessionResult.Succeeded || sessionResult.Value is null)
        {
            return OpcUaOperationResult.Failure(
                sessionResult.Error?.Code ?? "SessionNotConnected",
                sessionResult.Error?.Message ?? "OPC UA session is not connected.",
                sessionResult.Error?.Details);
        }

        try
        {
            var nodeId = ResolveNodeId(sessionResult.Value, request.Address);
            var writeValues = new WriteValueCollection
            {
                new()
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(request.Value))
                }
            };

            var response = await sessionResult.Value.WriteAsync(null, writeValues, cancellationToken);
            var result = response.Results[0];
            if (StatusCode.IsBad(result))
            {
                return OpcUaOperationResult.Failure(
                    result.ToString(),
                    $"OPC UA server rejected write to tag '{request.Address.Identifier}'.");
            }

            return OpcUaOperationResult.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToFailure(ex, "WriteFailed", $"Failed to write tag '{request.Address.Identifier}'.");
        }
    }

    /// <summary>
    /// Отписывается от событий, останавливает runtime и освобождает синхронизационные ресурсы.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync(CancellationToken.None);
    }

    /// <summary>
    /// Сохраняет снимок настроек, с которыми будут выполняться операции клиента.
    /// </summary>
    private OpcUaOptions SetOptions(OpcUaOptions? options)
    {
        var next = (options ?? _optionsMonitor.CurrentValue).Clone();

        lock (_optionsLock)
        {
            _options = next;
        }

        return next;
    }

    /// <summary>
    /// Возвращает активную сессию или результат ошибки, если клиент не подключен.
    /// </summary>
    private OpcUaOperationResult<ISession> GetConnectedSession()
    {
        lock (_sessionLock)
        {
            if (_session is { Connected: true })
            {
                return OpcUaOperationResult<ISession>.Success(_session);
            }
        }

        return OpcUaOperationResult<ISession>.Failure(
            "SessionNotConnected",
            "OPC UA session is not connected.");
    }

    /// <summary>
    /// Создает описание endpoint для режима None/Anonymous без discovery-запроса.
    /// </summary>
    private static EndpointDescription CreateAnonymousEndpointDescription(OpcUaOptions options)
        => new()
        {
            EndpointUrl = options.Client.EndpointUrl,
            SecurityMode = MessageSecurityMode.None,
            SecurityPolicyUri = SecurityPolicies.None,
            TransportProfileUri = Profiles.UaTcpTransport,
            Server = new ApplicationDescription
            {
                ApplicationName = options.Server.ApplicationName,
                ApplicationType = ApplicationType.Server,
                ApplicationUri = options.Server.ApplicationUri,
                ProductUri = options.Server.ProductUri,
                DiscoveryUrls = [options.Server.EndpointUrl]
            },
            UserIdentityTokens =
            [
                new UserTokenPolicy
                {
                    PolicyId = "Anonymous",
                    TokenType = UserTokenType.Anonymous,
                    SecurityPolicyUri = SecurityPolicies.None
                }
            ]
        };

    /// <summary>
    /// Преобразует адрес тега в NodeId, готовый для вызовов OPC UA SDK.
    /// </summary>
    private static NodeId ResolveNodeId(ISession session, OpcUaTagAddress address)
    {
        var namespaceIndex = session.NamespaceUris.GetIndex(address.NamespaceUri);
        if (namespaceIndex < 0 || namespaceIndex > ushort.MaxValue)
        {
            throw new InvalidOperationException($"Server does not publish namespace '{address.NamespaceUri}'.");
        }

        return CreateNodeId(address, (ushort)namespaceIndex);
    }

    /// <summary>
    /// Создает NodeId из namespace index, типа идентификатора и значения идентификатора.
    /// </summary>
    internal static NodeId CreateNodeId(OpcUaTagAddress address, ushort namespaceIndex)
    {
        if (string.Equals(address.IdentifierType, "Numeric", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(address.Identifier, out var numericIdentifier))
        {
            return new NodeId(numericIdentifier, namespaceIndex);
        }

        if (string.Equals(address.IdentifierType, "Guid", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(address.Identifier, out var guidIdentifier))
        {
            return new NodeId(guidIdentifier, namespaceIndex);
        }

        if (string.Equals(address.IdentifierType, "Opaque", StringComparison.OrdinalIgnoreCase))
        {
            return new NodeId(Convert.FromBase64String(address.Identifier), namespaceIndex);
        }

        return new NodeId(address.Identifier, namespaceIndex);
    }

    /// <summary>
    /// Преобразует исключение SDK в единый результат ошибки OPC UA.
    /// </summary>
    private static OpcUaOperationResult ToFailure(Exception exception, string code, string message)
    {
        if (exception is ServiceResultException serviceResultException)
        {
            return OpcUaOperationResult.Failure(
                serviceResultException.StatusCode.ToString(),
                message,
                serviceResultException.Message);
        }

        return OpcUaOperationResult.Failure(code, message, exception.Message);
    }

    /// <summary>
    /// Выбирает timestamp значения SDK или подставляет текущее время.
    /// </summary>
    private static DateTimeOffset ToTimestamp(DataValue value)
    {
        var timestamp = value.SourceTimestamp != DateTime.MinValue
            ? value.SourceTimestamp
            : value.ServerTimestamp;

        if (timestamp == DateTime.MinValue)
        {
            return DateTimeOffset.Now;
        }

        return new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)).ToLocalTime();
    }

    private static bool IsNoneSecurity(string mode)
        => string.Equals(mode, "None", StringComparison.OrdinalIgnoreCase);

    private static bool IsAnonymous(string mode)
        => string.Equals(mode, "Anonymous", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Обрабатывает KeepAlive SDK и публикует потерю соединения как состояние клиента.
    /// </summary>
    private void OnKeepAlive(ISession session, KeepAliveEventArgs e)
    {
        if (ServiceResult.IsGood(e.Status))
        {
            return;
        }

        OpcUaOptions currentOptions;
        lock (_optionsLock)
        {
            currentOptions = _options;
        }

        _states.Publish(OpcUaConnectionState.Create(
            OpcUaConnectionStatus.Reconnecting,
            currentOptions.Client.EndpointUrl,
            e.Status?.ToString()));
    }
}
