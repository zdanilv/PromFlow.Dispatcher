using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;
using Configurator.Infrastructure.OpcUa.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Client;
using System.Globalization;

namespace Configurator.Infrastructure.OpcUa.Client;

#pragma warning disable CS0618
/// <summary>
/// Просматривает адресное пространство OPC UA endpoint и преобразует найденные узлы в модели импорта тегов.
/// </summary>
public sealed class OpcUaTagBrowserService : IOpcUaTagBrowserService
{
    private const string DefaultEndpoint = "opc.tcp://localhost:4840";
    private readonly OpcUaApplicationConfigurationFactory _configurationFactory;
    private readonly IOpcUaIdentityProvider _identityProvider;
    private readonly ILogger<OpcUaTagBrowserService> _logger;
    private readonly IOptionsMonitor<OpcUaOptions> _optionsMonitor;
    private readonly IOpcUaSecurityProvider _securityProvider;
    private readonly ITelemetryContext _telemetry = DefaultTelemetry.Create(_ => { });

    /// <summary>
    /// Создает browser-сервис для просмотра endpoint-ов и импорта узлов как тегов.
    /// </summary>
    public OpcUaTagBrowserService(
        IOptionsMonitor<OpcUaOptions> optionsMonitor,
        OpcUaApplicationConfigurationFactory configurationFactory,
        IOpcUaSecurityProvider securityProvider,
        IOpcUaIdentityProvider identityProvider,
        ILogger<OpcUaTagBrowserService> logger)
    {
        _optionsMonitor = optionsMonitor;
        _configurationFactory = configurationFactory;
        _securityProvider = securityProvider;
        _identityProvider = identityProvider;
        _logger = logger;
    }

    /// <summary>
    /// Просматривает адресное пространство OPC UA endpoint и возвращает найденные узлы.
    /// </summary>
    public async Task<OpcUaOperationResult<OpcUaBrowseResult>> BrowseAsync(
        OpcUaBrowseRequest request,
        CancellationToken cancellationToken = default)
    {
        var options = (request.Options ?? _optionsMonitor.CurrentValue).Clone();
        options.Client.EndpointUrl = NormalizeEndpoint(request.EndpointUrl, options.Client.EndpointUrl);
        options.Client.Enabled = true;

        var security = _securityProvider.GetSecurity(options);
        var identity = _identityProvider.GetIdentity(options);
        if (!IsNoneSecurity(security.Mode) || security.CertificatesEnabled || !IsAnonymous(identity.Mode))
        {
            return OpcUaOperationResult<OpcUaBrowseResult>.Failure(
                "SecurityNotSupported",
                "Only Security.Mode=None, Authentication.Mode=Anonymous and Certificates.Enabled=false are supported in v1.");
        }

        ISession? session = null;
        try
        {
            session = await CreateSessionAsync(options, cancellationToken);
            // Если стартовый узел не задан, начинаем с ObjectsFolder: это ожидаемая точка для пользовательских тегов,
            // а не служебная часть адресного пространства OPC UA сервера.
            var counter = 0;
            var root = BrowseNode(
                session,
                ObjectIds.ObjectsFolder,
                "Objects",
                request.Target,
                depth: 0,
                maxDepth: Math.Max(1, request.MaxDepth),
                maxNodes: Math.Max(1, request.MaxNodes),
                counter: ref counter,
                cancellationToken);

            return OpcUaOperationResult<OpcUaBrowseResult>.Success(new OpcUaBrowseResult
            {
                Nodes = [root],
                Status = $"Connected. Nodes read: {counter}."
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to browse OPC UA endpoint {EndpointUrl}", options.Client.EndpointUrl);
            return OpcUaOperationResult<OpcUaBrowseResult>.Failure(
                "BrowseFailed",
                "Failed to browse OPC UA endpoint.",
                ex.Message);
        }
        finally
        {
            if (session is not null)
            {
                await session.CloseAsync(closeChannel: true, CancellationToken.None);
                session.Dispose();
            }
        }
    }

    /// <summary>
    /// Запрашивает у OPC UA сервера список доступных endpoint-ов.
    /// </summary>
    public async Task<OpcUaOperationResult<OpcUaEndpointDiscoveryResult>> DiscoverEndpointsAsync(
        OpcUaEndpointDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        var options = (request.Options ?? _optionsMonitor.CurrentValue).Clone();
        var endpointUrl = NormalizeEndpoint(request.EndpointUrl, options.Client.EndpointUrl);
        var discovered = new Dictionary<string, OpcUaEndpointProfile>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        void AddEndpoint(string url, string source)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            var normalized = url.Trim();
            discovered[normalized] = new OpcUaEndpointProfile
            {
                Name = EndpointName(normalized),
                EndpointUrl = normalized,
                Source = source
            };
        }

        // В ответ добавляем все известные endpoint-адреса и результаты discovery,
        // чтобы пользователь мог выбрать любой вариант без повторного ручного ввода.
        AddEndpoint(options.Client.EndpointUrl, "Client");
        AddEndpoint(options.Server.EndpointUrl, "Server");
        AddEndpoint(endpointUrl, "Manual");

        foreach (var endpoint in options.KnownEndpoints)
        {
            AddEndpoint(endpoint.EndpointUrl, endpoint.Source);
        }

        async Task DiscoverSourceAsync(string url, string source)
        {
            try
            {
                foreach (var endpoint in await DiscoverFromAsync(url, TimeSpan.FromSeconds(5), cancellationToken))
                {
                    AddEndpoint(endpoint, source);
                }
            }
            catch (TimeoutException)
            {
                warnings.Add($"Discovery timeout for {url}.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to discover OPC UA endpoints from {EndpointUrl}", url);
                warnings.Add($"Discovery failed for {url}: {ex.Message}");
            }
        }

        await DiscoverSourceAsync(endpointUrl, "Discovery");

        if (!SameEndpoint(endpointUrl, DefaultEndpoint))
        {
            await DiscoverSourceAsync(DefaultEndpoint, "Local Discovery");
        }

        var endpoints = discovered.Values
            .OrderBy(endpoint => endpoint.EndpointUrl, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var status = warnings.Count == 0
            ? $"Endpoints found: {endpoints.Length}."
            : $"Endpoints found: {endpoints.Length}. {warnings[0]}";

        return OpcUaOperationResult<OpcUaEndpointDiscoveryResult>.Success(new OpcUaEndpointDiscoveryResult
        {
            Endpoints = endpoints,
            Status = status,
            Warnings = warnings
        });
    }

    /// <summary>
    /// Создает временную OPC UA сессию для browsing или discovery без изменения основного runtime.
    /// </summary>
    private async Task<ISession> CreateSessionAsync(
        OpcUaOptions options,
        CancellationToken cancellationToken)
    {
        var configuration = _configurationFactory.CreateClient(options);
        configuration.TransportQuotas.OperationTimeout = Math.Max(1000, options.Client.ConnectTimeoutMilliseconds);

        var endpoint = new ConfiguredEndpoint(
            collection: null,
            CreateAnonymousEndpointDescription(options),
            EndpointConfiguration.Create(configuration));

        var sessionFactory = new DefaultSessionFactory(_telemetry);
        return await sessionFactory.CreateAsync(
            configuration,
            endpoint,
            updateBeforeConnect: false,
            sessionName: $"{options.Client.ApplicationName} Browser",
            sessionTimeout: (uint)Math.Max(1000, options.Client.SessionTimeoutMilliseconds),
            identity: null,
            preferredLocales: null,
            cancellationToken);
    }

    /// <summary>
    /// Читает один узел адресного пространства и строит модель для UI.
    /// </summary>
    private OpcUaBrowseNode BrowseNode(
        ISession session,
        NodeId nodeId,
        string name,
        OpcUaImportTarget target,
        int depth,
        int maxDepth,
        int maxNodes,
        ref int counter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var node = new OpcUaBrowseNode
        {
            Name = name,
            NodeId = nodeId.ToString(),
            NodeClass = depth == 0 ? "Object" : string.Empty,
            DataType = "none"
        };

        if (depth >= maxDepth || counter >= maxNodes)
        {
            return node;
        }

        var references = BrowseChildren(session, nodeId);
        foreach (var reference in references.OrderBy(item => item.DisplayName.Text, StringComparer.OrdinalIgnoreCase))
        {
            if (counter >= maxNodes)
            {
                break;
            }

            var childNodeId = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
            if (childNodeId is null)
            {
                continue;
            }

            counter++;
            var child = CreateBrowseNode(session, childNodeId, reference, target);

            if (reference.NodeClass is NodeClass.Object or NodeClass.View)
            {
                var folder = BrowseNode(
                    session,
                    childNodeId,
                    child.Name,
                    target,
                    depth + 1,
                    maxDepth,
                    maxNodes,
                    ref counter,
                    cancellationToken);

                folder.NodeClass = child.NodeClass;
                folder.NodeId = child.NodeId;
                folder.Comment = child.Comment;
                node.Children.Add(folder);
                continue;
            }

            if (reference.NodeClass == NodeClass.Variable)
            {
                node.Children.Add(child);
            }
        }

        return node;
    }

    /// <summary>
    /// Рекурсивно читает дочерние узлы с учетом ограничения глубины.
    /// </summary>
    private static ReferenceDescriptionCollection BrowseChildren(ISession session, NodeId nodeId)
    {
        session.Browse(
            null,
            null,
            nodeId,
            0,
            BrowseDirection.Forward,
            ReferenceTypeIds.HierarchicalReferences,
            true,
            (uint)(NodeClass.Object | NodeClass.Variable | NodeClass.View),
            out _,
            out var references);

        return references;
    }

    /// <summary>
    /// Преобразует SDK-описание узла в модель browse-дерева приложения.
    /// </summary>
    private OpcUaBrowseNode CreateBrowseNode(
        ISession session,
        NodeId nodeId,
        ReferenceDescription reference,
        OpcUaImportTarget target)
    {
        var nodeClass = reference.NodeClass.ToString();
        var displayName = string.IsNullOrWhiteSpace(reference.DisplayName.Text)
            ? reference.BrowseName.Name
            : reference.DisplayName.Text;

        if (reference.NodeClass != NodeClass.Variable)
        {
            return new OpcUaBrowseNode
            {
                Name = displayName,
                NodeId = nodeId.ToString(),
                NodeClass = nodeClass,
                DataType = "none",
                Comment = ReadDescription(session, nodeId)
            };
        }

        // Выбираем только scalar-переменные с поддерживаемым типом данных:
        // такие узлы можно безопасно импортировать как теги приложения.
        var metadata = ReadVariableMetadata(session, nodeId);
        var access = ReadAccess(session, nodeId);
        var supported = OpcUaDataTypeSupport.IsSupported(metadata.NormalizedDataType);
        var selectable = supported && metadata.IsScalar && IsAccessCompatible(access, target);
        var address = supported
            ? CreateAddress(session, nodeId, metadata.NormalizedDataType)
            : null;

        return new OpcUaBrowseNode
        {
            Name = displayName,
            NodeId = nodeId.ToString(),
            Address = address,
            NodeClass = nodeClass,
            DataType = metadata.DataType,
            NormalizedDataType = metadata.NormalizedDataType,
            ValueRank = metadata.ValueRank,
            Access = access,
            Comment = ReadDescription(session, nodeId),
            IsVariable = true,
            IsSelectable = selectable,
            UnsupportedReason = selectable
                ? string.Empty
                : UnsupportedReason(metadata, access, target, supported)
        };
    }

    /// <summary>
    /// Создает адрес тега из NodeId и метаданных переменной.
    /// </summary>
    private static OpcUaTagAddress CreateAddress(ISession session, NodeId nodeId, string dataType)
    {
        var namespaceUri = session.NamespaceUris.GetString(nodeId.NamespaceIndex) ?? string.Empty;
        return new OpcUaTagAddress(
            namespaceUri,
            FormatIdentifier(nodeId),
            dataType,
            GetIdentifierType(nodeId));
    }

    /// <summary>
    /// Читает тип данных, доступ и описание переменной перед импортом.
    /// </summary>
    private static VariableMetadata ReadVariableMetadata(ISession session, NodeId nodeId)
    {
        var dataType = ReadAttribute(session, nodeId, Attributes.DataType) as NodeId;
        var valueRank = ReadValueRank(session, nodeId);
        var displayType = DataTypeName(dataType);
        var value = ReadAttribute(session, nodeId, Attributes.Value);
        var inferredType = IsScalarValue(value) ? OpcUaDataTypeSupport.FromClrValue(value) : "Object";
        var normalizedType = OpcUaDataTypeSupport.IsSupported(displayType)
            ? displayType
            : inferredType;

        return new VariableMetadata(
            displayType,
            normalizedType,
            valueRank,
            IsScalarRank(valueRank) && (value is null || IsScalarValue(value)));
    }

    /// <summary>
    /// Преобразует маску доступа OPC UA в права чтения и записи приложения.
    /// </summary>
    private static OpcUaTagAccess ReadAccess(ISession session, NodeId nodeId)
    {
        var value = ReadAttribute(session, nodeId, Attributes.UserAccessLevel)
                    ?? ReadAttribute(session, nodeId, Attributes.AccessLevel);

        if (value is not byte accessLevel)
        {
            return OpcUaTagAccess.None;
        }

        var canRead = (accessLevel & AccessLevels.CurrentRead) == AccessLevels.CurrentRead;
        var canWrite = (accessLevel & AccessLevels.CurrentWrite) == AccessLevels.CurrentWrite;

        return (canRead, canWrite) switch
        {
            (true, true) => OpcUaTagAccess.ReadWrite,
            (true, false) => OpcUaTagAccess.Read,
            (false, true) => OpcUaTagAccess.Write,
            _ => OpcUaTagAccess.None
        };
    }

    /// <summary>
    /// Читает описание узла, если сервер его публикует.
    /// </summary>
    private static string ReadDescription(ISession session, NodeId nodeId)
    {
        var value = ReadAttribute(session, nodeId, Attributes.Description);
        return value switch
        {
            LocalizedText text => text.Text ?? string.Empty,
            string text => text,
            _ => string.Empty
        };
    }

    /// <summary>
    /// Безопасно читает атрибут узла и возвращает null при ошибке доступа.
    /// </summary>
    private static object? ReadAttribute(ISession session, NodeId nodeId, uint attributeId)
    {
        var nodesToRead = new ReadValueIdCollection
        {
            new()
            {
                NodeId = nodeId,
                AttributeId = attributeId
            }
        };

        session.Read(
            null,
            0,
            TimestampsToReturn.Neither,
            nodesToRead,
            out var results,
            out _);

        if (results.Count == 0 || StatusCode.IsBad(results[0].StatusCode))
        {
            return null;
        }

        return results[0].Value;
    }

    /// <summary>
    /// Создает endpoint-описание для None/Anonymous без отдельного discovery.
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
                new()
                {
                    PolicyId = "Anonymous",
                    TokenType = UserTokenType.Anonymous,
                    SecurityPolicyUri = SecurityPolicies.None
                }
            ]
        };

    /// <summary>
    /// Форматирует идентификатор NodeId в строку, сохраняемую в настройках.
    /// </summary>
    private static string FormatIdentifier(NodeId nodeId)
    {
        return nodeId.IdType switch
        {
            IdType.Numeric => Convert.ToString(nodeId.Identifier, CultureInfo.InvariantCulture) ?? string.Empty,
            IdType.Guid => nodeId.Identifier?.ToString() ?? string.Empty,
            IdType.Opaque => Convert.ToBase64String((byte[])nodeId.Identifier),
            _ => nodeId.Identifier?.ToString() ?? string.Empty
        };
    }

    /// <summary>
    /// Определяет тип идентификатора NodeId для последующего восстановления адреса.
    /// </summary>
    private static string GetIdentifierType(NodeId nodeId)
    {
        return nodeId.IdType switch
        {
            IdType.Numeric => "Numeric",
            IdType.Guid => "Guid",
            IdType.Opaque => "Opaque",
            _ => "String"
        };
    }

    /// <summary>
    /// Проверяет, подходит ли доступ узла для выбранной цели импорта.
    /// </summary>
    private static bool IsAccessCompatible(OpcUaTagAccess access, OpcUaImportTarget target)
    {
        return target switch
        {
            OpcUaImportTarget.ServerTelemetry => access is OpcUaTagAccess.Read or OpcUaTagAccess.ReadWrite,
            OpcUaImportTarget.ClientCommand => access is OpcUaTagAccess.Write or OpcUaTagAccess.ReadWrite,
            OpcUaImportTarget.Both => access is OpcUaTagAccess.Read or OpcUaTagAccess.Write or OpcUaTagAccess.ReadWrite,
            _ => false
        };
    }

    /// <summary>
    /// Выполняет discovery по одному адресу и возвращает найденные endpoint-ы.
    /// </summary>
    private static async Task<IReadOnlyList<string>> DiscoverFromAsync(
        string endpointUrl,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpointUrl))
        {
            return [];
        }

        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var uri))
        {
            throw new UriFormatException($"Endpoint URL '{endpointUrl}' is invalid.");
        }

        return await Task.Run(() =>
            {
                using var client = DiscoveryClient.Create(uri);
                var endpoints = client.GetEndpoints(null);
                return (IReadOnlyList<string>)endpoints
                    .Where(endpoint => string.Equals(endpoint.SecurityPolicyUri, SecurityPolicies.None, StringComparison.Ordinal)
                                       && endpoint.SecurityMode == MessageSecurityMode.None)
                    .Select(endpoint => endpoint.EndpointUrl)
                    .Where(url => !string.IsNullOrWhiteSpace(url))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            },
            cancellationToken).WaitAsync(timeout, cancellationToken);
    }

    /// <summary>
    /// Читает ValueRank, чтобы отличить scalar-переменные от массивов.
    /// </summary>
    private static int ReadValueRank(ISession session, NodeId nodeId)
    {
        var value = ReadAttribute(session, nodeId, Attributes.ValueRank);
        return value is int valueRank ? valueRank : ValueRanks.Scalar;
    }

    private static bool IsScalarRank(int valueRank)
        => valueRank == ValueRanks.Scalar || valueRank == ValueRanks.Any;

    private static bool IsScalarValue(object? value)
        => value is not null and not Array and not ExtensionObject and not VariantCollection;

    /// <summary>
    /// Преобразует NodeId типа данных в имя, понятное настройкам приложения.
    /// </summary>
    private static string DataTypeName(NodeId? dataType)
    {
        if (dataType is null) return "Object";
        if (dataType == DataTypeIds.Boolean) return "Boolean";
        if (dataType == DataTypeIds.SByte) return "SByte";
        if (dataType == DataTypeIds.Byte) return "Byte";
        if (dataType == DataTypeIds.Int16) return "Int16";
        if (dataType == DataTypeIds.UInt16) return "UInt16";
        if (dataType == DataTypeIds.Int32) return "Int32";
        if (dataType == DataTypeIds.UInt32) return "UInt32";
        if (dataType == DataTypeIds.Int64) return "Int64";
        if (dataType == DataTypeIds.UInt64) return "UInt64";
        if (dataType == DataTypeIds.Float) return "Float";
        if (dataType == DataTypeIds.Double) return "Double";
        if (dataType == DataTypeIds.String) return "String";
        return "Object";
    }

    /// <summary>
    /// Формирует причину, по которой найденный узел нельзя импортировать как тег.
    /// </summary>
    private static string UnsupportedReason(
        VariableMetadata metadata,
        OpcUaTagAccess access,
        OpcUaImportTarget target,
        bool supported)
    {
        if (!metadata.IsScalar)
        {
            return "Arrays and structures are not supported in v1.";
        }

        if (!supported)
        {
            return metadata.DataType == "Object"
                ? "Complex object has no scalar Boolean, number or String value."
                : $"Data type '{metadata.DataType}' is not supported in v1.";
        }

        if (!IsAccessCompatible(access, target))
        {
            return "Node does not have the read/write access required for this import target.";
        }

        return string.Empty;
    }

    private static string NormalizeEndpoint(string value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? NormalizeEndpoint(fallback, DefaultEndpoint) : value.Trim();

    private static bool SameEndpoint(string left, string right)
        => string.Equals(NormalizeEndpoint(left, DefaultEndpoint), NormalizeEndpoint(right, DefaultEndpoint), StringComparison.OrdinalIgnoreCase);

    private static string EndpointName(string endpointUrl)
    {
        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var uri))
        {
            return endpointUrl;
        }

        return $"{uri.Host}:{uri.Port}";
    }

    private static bool IsNoneSecurity(string mode)
        => string.Equals(mode, "None", StringComparison.OrdinalIgnoreCase);

    private static bool IsAnonymous(string mode)
        => string.Equals(mode, "Anonymous", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Хранит метаданные OPC UA переменной, нужные для импорта тега и проверки типа данных.
    /// </summary>
    private sealed record VariableMetadata(
        string DataType,
        string NormalizedDataType,
        int ValueRank,
        bool IsScalar);
}
#pragma warning restore CS0618
