using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;

namespace Configurator.Application.Services.OpcUa.Tags;

/// <summary>
/// Описывает адрес OPC UA тега: NodeId, namespace, тип данных и права доступа.
/// </summary>
public sealed record OpcUaTagAddress
{
    /// <summary>
    /// Создает пустой адрес для binding и постепенного заполнения формы.
    /// </summary>
    public OpcUaTagAddress()
    {
    }

    /// <summary>
    /// Создает адрес OPC UA тега из namespace, идентификатора и описания типа данных.
    /// </summary>
    public OpcUaTagAddress(
        string namespaceUri,
        string identifier,
        string? dataType = null,
        string identifierType = "String")
    {
        NamespaceUri = namespaceUri;
        Identifier = identifier;
        DataType = dataType;
        IdentifierType = identifierType;
    }

    /// <summary>
    /// Namespace URI, в котором расположен узел OPC UA.
    /// </summary>
    public string NamespaceUri { get; set; } = string.Empty;

    /// <summary>
    /// Идентификатор узла или точки данных внутри namespace.
    /// </summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>
    /// Тип данных, в который значение преобразуется при чтении или записи.
    /// </summary>
    public string? DataType { get; set; }

    /// <summary>
    /// Тип идентификатора NodeId, например String или Numeric.
    /// </summary>
    public string IdentifierType { get; set; } = "String";

    /// <summary>
    /// Определяет, что тег можно использовать для чтения или записи.
    /// </summary>
    public bool IsEmpty
        => string.IsNullOrWhiteSpace(NamespaceUri) || string.IsNullOrWhiteSpace(Identifier);
}
