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
/// Описывает тег OPC UA, сохраненный в настройках клиента или сервера.
/// </summary>
public sealed class OpcUaConfiguredTag
{
    /// <summary>
    /// Отображаемое имя элемента в UI и настройках.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Адрес тега или точки данных в протоколе.
    /// </summary>
    public OpcUaTagAddress Address { get; set; } = new();

    /// <summary>
    /// Права доступа тега: чтение, запись или оба режима.
    /// </summary>
    public OpcUaTagAccess Access { get; set; } = OpcUaTagAccess.Read;

    /// <summary>
    /// Комментарий пользователя или описание, импортированное из OPC UA узла.
    /// </summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>
    /// Начальное значение, которое сервер публикует после запуска.
    /// </summary>
    public string? InitialValue { get; set; }

    /// <summary>
    /// Показывает, можно ли читать значение тега.
    /// </summary>
    public bool IsReadable
        => Access is OpcUaTagAccess.Read or OpcUaTagAccess.ReadWrite;

    /// <summary>
    /// Показывает, можно ли записывать значение тега.
    /// </summary>
    public bool IsWritable
        => Access is OpcUaTagAccess.Write or OpcUaTagAccess.ReadWrite;

    /// <summary>
    /// Создает независимую копию объекта, чтобы runtime мог работать со снимком настроек без побочных изменений.
    /// </summary>
    public OpcUaConfiguredTag Clone()
    {
        return new OpcUaConfiguredTag
        {
            Name = Name,
            Address = new OpcUaTagAddress(
                Address.NamespaceUri,
                Address.Identifier,
                Address.DataType,
                Address.IdentifierType),
            Access = Access,
            Comment = Comment,
            InitialValue = InitialValue
        };
    }
}
