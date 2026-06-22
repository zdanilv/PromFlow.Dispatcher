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
    bool IsSystem = false,
    bool PreferPulseWriteMode = false);

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
                    categories.Length == 1 ? categories[0] : RouteMapSignalElementCategory.Common,
                    PreferPulseWriteMode: group.Any(item => item.PreferPulseWriteMode));
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

    private static IEnumerable<(string ObjectName, RouteMapSignalElementCategory Category, SignalBinding Binding, bool PreferPulseWriteMode)> EnumerateBindings(
        RouteMapDefinition definition)
    {
        if (definition.TopBar is not null)
        {
            yield return ("TopBar.АВТОМАТ", RouteMapSignalElementCategory.TopBar, definition.TopBar.Automatic.Binding, false);
            yield return ("TopBar.РУЧНОЙ", RouteMapSignalElementCategory.TopBar, definition.TopBar.Manual.Binding, false);
            yield return (
                "TopBar.АВАРИЯ",
                RouteMapSignalElementCategory.TopBar,
                definition.TopBar.Emergency.Binding,
                false);
        }

        foreach (var node in definition.Nodes)
        {
            foreach (var binding in node.Bindings)
            {
                if (IsDeprecatedSignalRole(binding.Role))
                    continue;

                yield return ($"Узел {node.Id}", RouteMapSignalElementCategory.Node, binding, false);
            }
        }

        foreach (var segment in definition.Segments)
        {
            foreach (var binding in segment.Bindings)
            {
                if (IsDeprecatedSignalRole(binding.Role))
                    continue;

                yield return ($"Линия {segment.Id}", RouteMapSignalElementCategory.Segment, binding, false);
            }

            foreach (var fragment in (segment.ActiveFragments ?? []).OrderBy(fragment => fragment.Index))
            {
                var binding = fragment.Binding;
                if (IsDeprecatedSignalRole(binding.Role))
                    continue;

                yield return ($"Линия {segment.Id}, отрезок {fragment.Index}", RouteMapSignalElementCategory.Segment, binding, false);
            }
        }

        foreach (var vehicle in definition.Vehicles)
        {
            foreach (var binding in vehicle.Bindings)
            {
                if (IsDeprecatedSignalRole(binding.Role))
                    continue;

                yield return ($"Объект {vehicle.Id}", RouteMapSignalElementCategory.Vehicle, binding, false);
            }
        }

        foreach (var card in definition.MapEquipment)
        {
            foreach (var binding in card.Bindings)
            {
                if (IsDeprecatedSignalRole(binding.Role))
                    continue;

                yield return (
                    $"Карточка {card.Id}",
                    RouteMapSignalElementCategory.Card,
                    binding,
                    false);
            }
        }
    }

    private static bool IsDeprecatedSignalRole(SignalBindingRole role) => role is
        SignalBindingRole.State or
        SignalBindingRole.StartOffFeedback or
        SignalBindingRole.StopOffFeedback or
        SignalBindingRole.TargetOffFeedback or
        SignalBindingRole.LoaderOffFeedback or
        SignalBindingRole.AutomaticModeOffFeedback or
        SignalBindingRole.ManualModeOffFeedback or
        SignalBindingRole.EmergencyOffFeedback;
}
