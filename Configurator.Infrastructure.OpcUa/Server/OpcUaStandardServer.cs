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

namespace Configurator.Infrastructure.OpcUa.Server;

/// <summary>
/// Связывает стандартный сервер OPC UA SDK с пользовательским менеджером узлов приложения.
/// </summary>
internal sealed class OpcUaStandardServer : StandardServer
{
    private readonly IOpcUaServerCommandSink _commandSink;
    private readonly OpcUaNodeOptions _nodes;
    private readonly TaskCompletionSource<OpcUaNodeManager> _nodeManagerReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Создает стандартный сервер SDK с настройками узлов и приемником command-записей.
    /// </summary>
    public OpcUaStandardServer(OpcUaNodeOptions nodes, IOpcUaServerCommandSink commandSink)
    {
        _nodes = nodes;
        _commandSink = commandSink;
    }

    /// <summary>
    /// Ожидает создания NodeManager, чтобы сервис мог обновлять значения тегов после старта сервера.
    /// </summary>
    public Task<OpcUaNodeManager> WaitForNodeManagerAsync(CancellationToken cancellationToken)
        => _nodeManagerReady.Task.WaitAsync(cancellationToken);

    /// <summary>
    /// Создает MasterNodeManager и подключает к нему пользовательский NodeManager приложения.
    /// </summary>
    protected override MasterNodeManager CreateMasterNodeManager(
        IServerInternal server,
        ApplicationConfiguration configuration)
    {
        var nodeManager = new OpcUaNodeManager(server, configuration, _nodes, _commandSink);
        _nodeManagerReady.TrySetResult(nodeManager);

        return new MasterNodeManager(server, configuration, null, new INodeManager[]
        {
            nodeManager
        });
    }
}
