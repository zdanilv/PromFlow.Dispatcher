using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.SignalMapping;

internal enum RouteMapSignalElementCategory
{
    System,
    TopBar,
    Node,
    Segment,
    Card,
    Vehicle,
    Common
}

internal sealed record RouteMapSignalInventoryItem(
    string SignalId,
    SignalValueType ExpectedType,
    ModbusDataAccess RequiredAccess,
    string Roles,
    string Objects,
    bool HasTypeConflict,
    RouteMapSignalElementCategory Category,
    bool IsSystem = false);

internal static class RouteMapSignalInventory
{
    public static IReadOnlyList<RouteMapSignalInventoryItem> Build(RouteMapDefinition definition)
    {
        var usages = EnumerateBindings(definition).ToArray();
        var items = usages
            .Where(usage => !string.IsNullOrWhiteSpace(usage.Binding.SignalId))
            .GroupBy(usage => usage.Binding.SignalId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                if (string.Equals(group.Key, "connection.status", StringComparison.OrdinalIgnoreCase))
                {
                    return new RouteMapSignalInventoryItem(
                        group.First().Binding.SignalId,
                        SignalValueType.String,
                        ModbusDataAccess.Read,
                        string.Join(", ", group.Select(item => item.Binding.Role).Distinct()),
                        string.Join(", ", group.Select(item => item.ObjectName).Distinct(StringComparer.Ordinal)),
                        HasTypeConflict: false,
                        Category: RouteMapSignalElementCategory.System,
                        IsSystem: true);
                }

                var types = group.Select(item => item.Binding.ValueType).Distinct().ToArray();
                var categories = group.Select(item => item.Category).Distinct().ToArray();
                var canRead = group.Any(item => item.Binding.Direction is SignalBindingDirection.Read or SignalBindingDirection.ReadWrite);
                var canWrite = group.Any(item => item.Binding.Direction is SignalBindingDirection.Write or SignalBindingDirection.ReadWrite);
                var access = (canRead, canWrite) switch
                {
                    (true, true) => ModbusDataAccess.ReadWrite,
                    (false, true) => ModbusDataAccess.Write,
                    _ => ModbusDataAccess.Read,
                };

                return new RouteMapSignalInventoryItem(
                    group.First().Binding.SignalId,
                    types[0],
                    access,
                    string.Join(", ", group.Select(item => item.Binding.Role).Distinct()),
                    string.Join(", ", group.Select(item => item.ObjectName).Distinct(StringComparer.Ordinal)),
                    types.Length > 1,
                    categories.Length == 1 ? categories[0] : RouteMapSignalElementCategory.Common);
            })
            .OrderBy(item => item.SignalId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (items.All(item => !string.Equals(item.SignalId, "connection.status", StringComparison.OrdinalIgnoreCase)))
        {
            items.Insert(0, new RouteMapSignalInventoryItem(
                "connection.status",
                SignalValueType.String,
                ModbusDataAccess.Read,
                "System",
                "Modbus runtime",
                HasTypeConflict: false,
                Category: RouteMapSignalElementCategory.System,
                IsSystem: true));
        }

        return items;
    }

    private static IEnumerable<(string ObjectName, RouteMapSignalElementCategory Category, SignalBinding Binding)> EnumerateBindings(
        RouteMapDefinition definition)
    {
        if (definition.TopBar is not null)
        {
            yield return ("TopBar.АВТОМАТ", RouteMapSignalElementCategory.TopBar, definition.TopBar.Automatic.Binding);
            yield return ("TopBar.РУЧНОЙ", RouteMapSignalElementCategory.TopBar, definition.TopBar.Manual.Binding);
            yield return ("TopBar.АВАРИЯ", RouteMapSignalElementCategory.TopBar, definition.TopBar.Emergency.Binding);
        }

        foreach (var node in definition.Nodes)
        {
            foreach (var binding in node.Bindings)
            {
                yield return ($"Узел {node.Id}", RouteMapSignalElementCategory.Node, binding);
            }
        }

        foreach (var segment in definition.Segments)
        {
            foreach (var binding in segment.Bindings)
            {
                yield return ($"Линия {segment.Id}", RouteMapSignalElementCategory.Segment, binding);
            }
        }

        foreach (var vehicle in definition.Vehicles)
        {
            foreach (var binding in vehicle.Bindings)
            {
                yield return ($"Объект {vehicle.Id}", RouteMapSignalElementCategory.Vehicle, binding);
            }
        }

        foreach (var card in definition.MapEquipment)
        {
            foreach (var binding in card.Bindings)
            {
                yield return ($"Карточка {card.Id}", RouteMapSignalElementCategory.Card, binding);
            }
        }
    }
}
