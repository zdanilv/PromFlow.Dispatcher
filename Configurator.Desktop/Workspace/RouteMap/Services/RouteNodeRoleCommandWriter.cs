using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.Services;

public static class RouteNodeRoleCommandWriter
{
    public static async Task DispatchTransitionAsync(
        RouteMapDefinition definition,
        IReadOnlyDictionary<string, RouteNodeRoleState> previous,
        IReadOnlyDictionary<string, RouteNodeRoleState> current,
        IEquipmentCommandDispatcher dispatcher)
    {
        var changes = CreateChanges(definition, previous, current);
        await Task.WhenAll(changes.Where(x => !x.Value).Select(x => DispatchAsync(dispatcher, x)));
        await Task.WhenAll(changes.Where(x => x.Value).Select(x => DispatchAsync(dispatcher, x)));
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

            writes.Add(dispatcher.DispatchAsync(
                new SignalWriteRequest(binding.SignalId, selector(after), binding.ValueType)));
        }

        return Task.WhenAll(writes);
    }

    private static IReadOnlyList<(SignalBinding Binding, bool Value)> CreateChanges(
        RouteMapDefinition definition,
        IReadOnlyDictionary<string, RouteNodeRoleState> previous,
        IReadOnlyDictionary<string, RouteNodeRoleState> current)
    {
        var changes = new List<(SignalBinding Binding, bool Value)>();
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
        ICollection<(SignalBinding Binding, bool Value)> changes)
    {
        if (previous == current)
            return;
        var binding = node.Bindings.FirstOrDefault(x => x.Role == role);
        if (binding is not null && binding.Direction != SignalBindingDirection.Read)
            changes.Add((binding, current));
    }

    private static Task DispatchAsync(
        IEquipmentCommandDispatcher dispatcher,
        (SignalBinding Binding, bool Value) change) =>
        dispatcher.DispatchAsync(new SignalWriteRequest(
            change.Binding.SignalId,
            change.Value,
            change.Binding.ValueType));
}
