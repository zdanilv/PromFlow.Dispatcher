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
    bool PreferPulseWriteMode = false,
    bool PreferHoldingRegisterBit = false,
    int? PreferredBitIndex = null,
    bool RequiresLatchedWriteMode = false,
    bool RequiresPulseWriteMode = false,
    bool RequiresExplicitPulseMapping = false,
    bool IsResetFeedback = false);

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
                if (IsSystemSignal(group.Key))
                {
                    return new RouteMapSignalInventoryItem(
                        group.First().Binding.SignalId,
                        SystemSignalType(group.Key),
                        ModbusDataAccess.Read,
                        string.Join(", ", group.Select(item => item.Binding.Role).Distinct()),
                        string.Join(", ", group.Select(item => item.ObjectName).Distinct(StringComparer.Ordinal)),
                        HasTypeConflict: false,
                        Category: RouteMapSignalElementCategory.System,
                        IsSystem: true);
                }

                var rawTypes = group.Select(item => item.Binding.ValueType).ToArray();
                var types = rawTypes
                    .Select(SignalModbusTypeCompatibility.NormalizeEquivalent)
                    .Distinct()
                    .ToArray();
                var categories = group.Select(item => item.Category).Distinct().ToArray();
                var hasUncheckedCommand = group.Any(item => item.Binding.Role == SignalBindingRole.UncheckedCommand);
                var hasCheckedCommand = group.Any(item => item.Binding.Role == SignalBindingRole.CheckedCommand);
                var hasResetCommand = group.Any(item => item.Binding.Role == SignalBindingRole.ResetCommand);
                var hasStopCommand = group.Any(item => item.Binding.Role == SignalBindingRole.StopCommand);
                var hasSelectorCommand = hasUncheckedCommand || hasCheckedCommand;
                var requiresLatchedWriteMode = hasSelectorCommand || hasStopCommand;
                var requiresPulseWriteMode = hasResetCommand || group.Any(item => item.PreferPulseWriteMode);
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
                    rawTypes.Contains(SignalValueType.Word) && types[0] == SignalValueType.UInt16
                        ? SignalValueType.Word
                        : types[0],
                    access,
                    string.Join(", ", group.Select(item => item.Binding.Role).Distinct()),
                    string.Join(", ", group.Select(item => item.ObjectName).Distinct(StringComparer.Ordinal)),
                    types.Length > 1,
                    categories.Length == 1 ? categories[0] : RouteMapSignalElementCategory.Common,
                    PreferPulseWriteMode: requiresPulseWriteMode,
                    PreferHoldingRegisterBit: hasSelectorCommand,
                    PreferredBitIndex: hasCheckedCommand && !hasUncheckedCommand ? 1 : hasSelectorCommand ? 0 : null,
                    RequiresLatchedWriteMode: requiresLatchedWriteMode,
                    RequiresPulseWriteMode: requiresPulseWriteMode,
                    RequiresExplicitPulseMapping: group.Any(item => item.Category == RouteMapSignalElementCategory.TopBar
                        && item.Binding.Role is SignalBindingRole.UnloadCommand or SignalBindingRole.RotationCommand or SignalBindingRole.LoadCommand),
                    IsResetFeedback: group.Any(item => item.Binding.Role == SignalBindingRole.CommandResetFeedback));
            })
            .OrderBy(item => item.SignalId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        EnsureSystemSignal(items, RouteMapSystemSignalIds.ConnectionStatus, SignalValueType.String);
        EnsureSystemSignal(items, RouteMapSystemSignalIds.ConnectionConnected, SignalValueType.Bool);
        EnsureSystemSignal(items, RouteMapSystemSignalIds.GlobalFault, SignalValueType.Bool, isInternal: false);

        return items;
    }

    private static void EnsureSystemSignal(
        List<RouteMapSignalInventoryItem> items,
        string signalId,
        SignalValueType valueType,
        bool isInternal = true)
    {
        if (items.Any(item => string.Equals(item.SignalId, signalId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        items.Insert(0, new RouteMapSignalInventoryItem(
            signalId,
            valueType,
            ModbusDataAccess.Read,
            "System",
            isInternal ? "Modbus runtime" : "RouteMap",
            HasTypeConflict: false,
            Category: RouteMapSignalElementCategory.System,
            IsSystem: isInternal));
    }

    private static bool IsSystemSignal(string signalId) =>
        string.Equals(signalId, RouteMapSystemSignalIds.ConnectionStatus, StringComparison.OrdinalIgnoreCase)
        || string.Equals(signalId, RouteMapSystemSignalIds.ConnectionConnected, StringComparison.OrdinalIgnoreCase);

    private static SignalValueType SystemSignalType(string signalId) =>
        string.Equals(signalId, RouteMapSystemSignalIds.ConnectionConnected, StringComparison.OrdinalIgnoreCase)
            ? SignalValueType.Bool
            : SignalValueType.String;

    private static IEnumerable<(string ObjectName, RouteMapSignalElementCategory Category, SignalBinding Binding, bool PreferPulseWriteMode)> EnumerateBindings(
        RouteMapDefinition definition)
    {
        if (definition.Display?.FrequencyBinding is { } frequency)
            yield return ("RouteMap.Частота (Hz)", RouteMapSignalElementCategory.Common, frequency, false);

        if (definition.TopBar is { } topBar)
        {
            var buttons = new (RouteTopBarButtonSettings Button, string Name, bool Pulse)[]
            {
                (topBar.Unload, topBar.Unload.Text, true),
                (topBar.Rotation, topBar.Rotation.Text, true),
                (topBar.Load, topBar.Load.Text, true),
                (topBar.Reset, "СБРОС", true),
                (topBar.Emergency, "АВАРИЯ", false),
            };
            foreach (var (button, name, pulse) in buttons)
            {
                yield return ($"TopBar.{name}", RouteMapSignalElementCategory.TopBar, button.Binding, pulse);
                if (button.EnabledBinding is { } enabled)
                    yield return ($"TopBar.{name}", RouteMapSignalElementCategory.TopBar, enabled, false);
                if (button.ResetFeedbackBinding is { } feedback)
                    yield return ($"TopBar.{name}", RouteMapSignalElementCategory.TopBar, feedback, false);
            }
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
                    binding.Role == SignalBindingRole.StartCommand && card.StartButtonKind == RouteCommandButtonKind.Momentary);
            }

            foreach (var parameter in card.Parameters)
            {
                yield return (
                    $"Карточка {card.Id}, настройка {parameter.Title}",
                    RouteMapSignalElementCategory.Card,
                    parameter.Binding,
                    false);
            }
        }
    }

    private static bool IsDeprecatedSignalRole(SignalBindingRole role) => role is
        SignalBindingRole.State or
        SignalBindingRole.TargetOffFeedback or
        SignalBindingRole.LoaderOffFeedback or
        SignalBindingRole.AutomaticModeOffFeedback or
        SignalBindingRole.ManualModeOffFeedback or
        SignalBindingRole.EmergencyOffFeedback;
}
