using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;
using Opc.Ua;
using Opc.Ua.Server;
using System.Globalization;

namespace Configurator.Infrastructure.OpcUa.Server;

/// <summary>
/// Создает адресное пространство встроенного OPC UA сервера и связывает узлы с локальными значениями тегов.
/// </summary>
internal sealed class OpcUaNodeManager : CustomNodeManager2
{
    private readonly IOpcUaServerCommandSink _commandSink;
    private readonly OpcUaNodeOptions _nodes;
    private readonly Dictionary<string, BaseDataVariableState> _variables = new(StringComparer.Ordinal);

    /// <summary>
    /// Создает NodeManager для адресного пространства встроенного OPC UA сервера.
    /// </summary>
    public OpcUaNodeManager(
        IServerInternal server,
        ApplicationConfiguration configuration,
        OpcUaNodeOptions nodes,
        IOpcUaServerCommandSink commandSink)
        : base(server, configuration, [nodes.NamespaceUri])
    {
        _nodes = nodes;
        _commandSink = commandSink;
    }

    /// <summary>
    /// Строит дерево объектов, папок и переменных сервера из текущих настроек тегов.
    /// </summary>
    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalRefs)
    {
        var device = CreateObject(null, _nodes.DeviceIdentifier, "DemoDevice");
        AddPredefinedNode(SystemContext, device);

        if (!externalRefs.TryGetValue(ObjectIds.ObjectsFolder, out var objectsFolderReferences))
        {
            externalRefs[ObjectIds.ObjectsFolder] = objectsFolderReferences = [];
        }

        objectsFolderReferences.Add(
            new NodeStateReference(ReferenceTypeIds.Organizes, false, device.NodeId));

        var telemetryFolder = CreateFolder(device, _nodes.TelemetryFolderIdentifier, "Telemetry");
        var commandsFolder = CreateFolder(device, _nodes.CommandsFolderIdentifier, "Commands");

        // Создаем командные переменные с обработчиком записи: внешние клиенты меняют их, а runtime получает событие.
        foreach (var tag in _nodes.TelemetryDefinitions())
        {
            CreateVariable(
                telemetryFolder,
                tag.Address.Identifier,
                ToBrowseName(tag),
                ParseInitialValue(tag),
                ToDataTypeId(tag.Address.DataType),
                AccessLevels.CurrentRead);
        }

        // Telemetry-теги доступны только для чтения: значения обновляет приложение, а не внешний OPC UA клиент.
        foreach (var tag in _nodes.CommandDefinitions())
        {
            CreateCommandVariable(
                commandsFolder,
                tag.Address.Identifier,
                ToBrowseName(tag),
                ParseInitialValue(tag),
                ToDataTypeId(tag.Address.DataType));
        }
    }

    /// <summary>
    /// Обновляет значение переменной сервера и публикует изменение в SDK.
    /// </summary>
    public OpcUaOperationResult UpdateTagValue(OpcUaTagAddress address, object? value)
    {
        if (address.IsEmpty)
        {
            return OpcUaOperationResult.Failure(
                "TagAddressEmpty",
                "Tag address must not be empty.");
        }

        lock (Lock)
        {
            if (!_variables.TryGetValue(address.Identifier, out var variable))
            {
                return OpcUaOperationResult.Failure(
                    "TagNotFound",
                    $"Tag '{address.Identifier}' was not found in the server address space.");
            }

            SetValue(variable, value);
        }

        return OpcUaOperationResult.Success();
    }

    /// <summary>
    /// Создает корневой объект устройства в адресном пространстве сервера.
    /// </summary>
    private BaseObjectState CreateObject(NodeState? parent, string identifier, string browseName)
    {
        var node = new BaseObjectState(parent)
        {
            NodeId = CreateNodeId(identifier),
            BrowseName = CreateQualifiedName(browseName),
            DisplayName = browseName,
            TypeDefinitionId = ObjectTypeIds.BaseObjectType
        };

        parent?.AddChild(node);
        return node;
    }

    /// <summary>
    /// Создает папку OPC UA и связывает ее с родительским объектом.
    /// </summary>
    private FolderState CreateFolder(NodeState parent, string identifier, string browseName)
    {
        var node = new FolderState(parent)
        {
            NodeId = CreateNodeId(identifier),
            BrowseName = CreateQualifiedName(browseName),
            DisplayName = browseName,
            TypeDefinitionId = ObjectTypeIds.FolderType
        };

        parent.AddChild(node);
        AddPredefinedNode(SystemContext, node);
        return node;
    }

    /// <summary>
    /// Создает writable-переменную command и подключает обработчик записи.
    /// </summary>
    private BaseDataVariableState CreateCommandVariable(
        NodeState parent,
        string identifier,
        string browseName,
        object value,
        NodeId dataType)
    {
        var node = CreateVariable(
            parent,
            identifier,
            browseName,
            value,
            dataType,
            AccessLevels.CurrentReadOrWrite);

        node.OnSimpleWriteValue = OnWriteCommandValue;
        return node;
    }

    /// <summary>
    /// Создает telemetry-переменную только для чтения с начальным значением.
    /// </summary>
    private BaseDataVariableState CreateVariable(
        NodeState parent,
        string identifier,
        string browseName,
        object value,
        NodeId dataType,
        byte accessLevel)
    {
        var node = new BaseDataVariableState(parent)
        {
            NodeId = CreateNodeId(identifier),
            BrowseName = CreateQualifiedName(browseName),
            DisplayName = browseName,
            TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
            Value = value,
            DataType = dataType,
            ValueRank = ValueRanks.Scalar,
            AccessLevel = accessLevel,
            UserAccessLevel = accessLevel,
            StatusCode = StatusCodes.Good,
            Timestamp = DateTime.UtcNow
        };

        parent.AddChild(node);
        AddPredefinedNode(SystemContext, node);
        _variables[identifier] = node;
        return node;
    }

    /// <summary>
    /// Обрабатывает запись command-переменной внешним клиентом.
    /// </summary>
    private ServiceResult OnWriteCommandValue(ISystemContext context, NodeState node, ref object value)
    {
        if (node is not BaseDataVariableState variable)
        {
            return StatusCodes.BadNodeIdInvalid;
        }

        if (!IsExpectedType(variable.DataType, value))
        {
            return StatusCodes.BadTypeMismatch;
        }

        SetValue(variable, value);
        _commandSink.PublishCommand(CreateCommandValue(variable, value));
        return ServiceResult.Good;
    }

    /// <summary>
    /// Преобразует записанное SDK-значение в модель command-тега приложения.
    /// </summary>
    private OpcUaTagValue CreateCommandValue(BaseDataVariableState variable, object? value)
        => new(
            new OpcUaTagAddress(
                _nodes.NamespaceUri,
                variable.NodeId.Identifier?.ToString() ?? string.Empty,
                GetDataTypeName(variable.DataType)),
            value,
            DateTimeOffset.Now,
            "Good");

    /// <summary>
    /// Записывает значение в переменную SDK и обновляет timestamp/status.
    /// </summary>
    private void SetValue(BaseDataVariableState variable, object? value)
    {
        variable.Value = value;
        variable.Timestamp = DateTime.UtcNow;
        variable.StatusCode = StatusCodes.Good;
        variable.ClearChangeMasks(SystemContext, false);
    }

    /// <summary>
    /// Проверяет, совместимо ли значение с типом переменной перед записью.
    /// </summary>
    private static bool IsExpectedType(NodeId dataType, object value)
    {
        return GetDataTypeName(dataType) switch
        {
            "Boolean" => value is bool,
            "SByte" => value is sbyte,
            "Byte" => value is byte,
            "Int16" => value is short,
            "UInt16" => value is ushort,
            "Int32" => value is int,
            "UInt32" => value is uint,
            "Int64" => value is long,
            "UInt64" => value is ulong,
            "Float" => value is float,
            "Double" => value is double,
            "String" => value is string,
            _ => false
        };
    }

    /// <summary>
    /// Определяет имя типа данных по NodeId переменной SDK.
    /// </summary>
    private static string? GetDataTypeName(NodeId dataType)
    {
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
    /// Создает NodeId сервера из namespace index и строкового идентификатора.
    /// </summary>
    private NodeId CreateNodeId(string identifier)
        => new(identifier, NamespaceIndex);

    /// <summary>
    /// Создает QualifiedName для browse-имени узла.
    /// </summary>
    private QualifiedName CreateQualifiedName(string browseName)
        => new(browseName, NamespaceIndex);

    /// <summary>
    /// Выделяет короткое browse-имя из полного идентификатора тега.
    /// </summary>
    private static string ToBrowseName(OpcUaConfiguredTag tag)
    {
        if (!string.IsNullOrWhiteSpace(tag.Name))
        {
            return tag.Name.Trim();
        }

        var identifier = tag.Address.Identifier;
        var separator = identifier.LastIndexOf('/');
        return separator >= 0 && separator < identifier.Length - 1
            ? identifier[(separator + 1)..]
            : identifier;
    }

    /// <summary>
    /// Преобразует начальное значение из настроек в тип переменной SDK.
    /// </summary>
    private static object ParseInitialValue(OpcUaConfiguredTag tag)
    {
        var text = tag.InitialValue ?? string.Empty;
        return OpcUaDataTypeSupport.TryParse(tag.Address.DataType, text, out var value, out _)
            ? value ?? string.Empty
            : OpcUaDataTypeSupport.DefaultInitialValue(tag.Address.DataType);
    }

    /// <summary>
    /// Преобразует имя типа данных в стандартный NodeId типа OPC UA.
    /// </summary>
    private static NodeId ToDataTypeId(string? dataType)
    {
        if (string.Equals(dataType, "Boolean", StringComparison.OrdinalIgnoreCase)) return DataTypeIds.Boolean;
        if (string.Equals(dataType, "SByte", StringComparison.OrdinalIgnoreCase)) return DataTypeIds.SByte;
        if (string.Equals(dataType, "Byte", StringComparison.OrdinalIgnoreCase)) return DataTypeIds.Byte;
        if (string.Equals(dataType, "Int16", StringComparison.OrdinalIgnoreCase)) return DataTypeIds.Int16;
        if (string.Equals(dataType, "UInt16", StringComparison.OrdinalIgnoreCase)) return DataTypeIds.UInt16;
        if (string.Equals(dataType, "Int32", StringComparison.OrdinalIgnoreCase)) return DataTypeIds.Int32;
        if (string.Equals(dataType, "UInt32", StringComparison.OrdinalIgnoreCase)) return DataTypeIds.UInt32;
        if (string.Equals(dataType, "Int64", StringComparison.OrdinalIgnoreCase)) return DataTypeIds.Int64;
        if (string.Equals(dataType, "UInt64", StringComparison.OrdinalIgnoreCase)) return DataTypeIds.UInt64;
        if (string.Equals(dataType, "Float", StringComparison.OrdinalIgnoreCase)) return DataTypeIds.Float;
        if (string.Equals(dataType, "Double", StringComparison.OrdinalIgnoreCase)) return DataTypeIds.Double;
        return DataTypeIds.String;
    }
}
