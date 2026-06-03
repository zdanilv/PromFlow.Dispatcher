using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;

namespace Configurator.Application.Services.OpcUa.Browsing;

/// <summary>
/// Описывает один узел, найденный при просмотре OPC UA адресного пространства.
/// </summary>
public sealed class OpcUaBrowseNode
{
    /// <summary>
    /// Отображаемое имя элемента в UI и настройках.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Строковое представление NodeId OPC UA узла.
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// Адрес тега или точки данных в протоколе.
    /// </summary>
    public OpcUaTagAddress? Address { get; set; }

    /// <summary>
    /// Класс узла OPC UA, например Object, Variable или Method.
    /// </summary>
    public string NodeClass { get; set; } = string.Empty;

    /// <summary>
    /// Тип данных, в который значение преобразуется при чтении или записи.
    /// </summary>
    public string DataType { get; set; } = "none";

    /// <summary>
    /// Нормализованное имя типа данных, поддерживаемое редактором тегов.
    /// </summary>
    public string NormalizedDataType { get; set; } = "none";

    /// <summary>
    /// ValueRank узла: scalar-значения импортируются, массивы помечаются как неподдерживаемые.
    /// </summary>
    public int ValueRank { get; set; } = -1;

    /// <summary>
    /// Права доступа тега: чтение, запись или оба режима.
    /// </summary>
    public OpcUaTagAccess Access { get; set; } = OpcUaTagAccess.None;

    /// <summary>
    /// Комментарий, который UI показывает рядом с найденным узлом.
    /// </summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>
    /// Причина, по которой узел нельзя выбрать для импорта.
    /// </summary>
    public string UnsupportedReason { get; set; } = string.Empty;

    /// <summary>
    /// Показывает, что узел является переменной и может иметь значение.
    /// </summary>
    public bool IsVariable { get; set; }

    /// <summary>
    /// Показывает, что узел можно импортировать как тег выбранной роли.
    /// </summary>
    public bool IsSelectable { get; set; }

    /// <summary>
    /// Дочерние узлы, найденные при рекурсивном просмотре.
    /// </summary>
    public List<OpcUaBrowseNode> Children { get; set; } = [];
}
