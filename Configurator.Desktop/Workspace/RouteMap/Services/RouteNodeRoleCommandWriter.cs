using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.Services;

public static class RouteNodeRoleCommandWriter
{
    public static Task DispatchTransitionAsync(
        RouteMapDefinition definition,
        IReadOnlyDictionary<string, RouteNodeRoleState> previous,
        IReadOnlyDictionary<string, RouteNodeRoleState> current,
        IEquipmentCommandDispatcher dispatcher)
        => DispatchTransitionAsync(CreateTransitionRequests(definition, previous, current), dispatcher);

    internal static async Task DispatchTransitionAsync(
        IReadOnlyList<SignalWriteRequest> requests,
        IEquipmentCommandDispatcher dispatcher)
    {
        await Task.WhenAll(requests.Where(request => request.Value is false).Select(request => dispatcher.DispatchAsync(request)));
        await Task.WhenAll(requests.Where(request => request.Value is true).Select(request => dispatcher.DispatchAsync(request)));
    }

    public static Task DispatchChangesAsync(
        RouteMapDefinition definition,
        IReadOnlyDictionary<string, RouteNodeRoleState> previous,
        IReadOnlyDictionary<string, RouteNodeRoleState> current,
        SignalBindingRole role,
        Func<RouteNodeRoleState, bool> selector,
        IEquipmentCommandDispatcher dispatcher)
    {
        var writes = new List<Task>();
        foreach (var node in definition.Nodes)
        {
            if (!previous.TryGetValue(node.Id, out var before) ||
                !current.TryGetValue(node.Id, out var after) ||
                selector(before) == selector(after))
                continue;

            var binding = node.Bindings.FirstOrDefault(x => x.Role == role);
            if (binding is null || binding.Direction == SignalBindingDirection.Read)
                continue;

            writes.Add(dispatcher.DispatchAsync(CreateRequest(binding, selector(after))));
        }

        return Task.WhenAll(writes);
    }

    internal static IReadOnlyList<SignalWriteRequest> CreateTransitionRequests(
        RouteMapDefinition definition,
        IReadOnlyDictionary<string, RouteNodeRoleState> previous,
        IReadOnlyDictionary<string, RouteNodeRoleState> current)
    {
        var changes = new List<SignalWriteRequest>();
        foreach (var node in definition.Nodes)
        {
            if (!previous.TryGetValue(node.Id, out var before) || !current.TryGetValue(node.Id, out var after))
                continue;
            AddChange(node, SignalBindingRole.TargetCommand, before.IsTarget, after.IsTarget, changes);
            AddChange(node, SignalBindingRole.LoaderCommand, before.IsLoader, after.IsLoader, changes);
        }
        return changes;
    }

    private static void AddChange(
        RouteNode node,
        SignalBindingRole role,
        bool previous,
        bool current,
        ICollection<SignalWriteRequest> changes)
    {
        if (previous == current)
            return;
        var binding = node.Bindings.FirstOrDefault(x => x.Role == role);
        if (binding is not null && binding.Direction != SignalBindingDirection.Read)
            changes.Add(CreateRequest(binding, current));
    }

    private static SignalWriteRequest CreateRequest(SignalBinding binding, bool value) =>
        new(binding.SignalId, value, binding.ValueType)
        {
            RequiredWriteMode = string.Equals(binding.SignalId, RouteMapSeed.WashTargetSignalId, StringComparison.OrdinalIgnoreCase)
                ? ModbusWriteMode.Latched
                : null,
        };
}
