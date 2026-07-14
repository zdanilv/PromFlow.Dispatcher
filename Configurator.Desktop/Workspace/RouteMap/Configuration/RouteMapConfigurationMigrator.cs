using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.Configuration;

public sealed record RouteMapConfigurationMigrationResult(
    RouteMapConfigurationDocument Document,
    bool WasMigrated);

public sealed class RouteMapConfigurationMigrator
{
    private static readonly string[] StandardNodeIds =
    [
        "dead_end_lower",
        "bsu_1",
        "bsu_2",
        "concrete_bucket",
        "dead_end_upper",
    ];

    private static readonly string[] StandardSegmentIds =
    [
        "lower_dead_end_to_bsu1",
        "active_bsu1_bsu2",
        "bsu2_to_bucket",
        "bucket_to_upper_dead_end",
    ];

    public RouteMapConfigurationMigrationResult Migrate(RouteMapConfigurationDocument document)
    {
        if (document.SchemaVersion > RouteMapConfigurationDocument.CurrentSchemaVersion || document.SchemaVersion < 1)
            throw new InvalidDataException(
                $"Невозможно мигрировать RouteMap schemaVersion={document.SchemaVersion} в версию {RouteMapConfigurationDocument.CurrentSchemaVersion}.");

        var wasMigrated = false;
        while (document.SchemaVersion < RouteMapConfigurationDocument.CurrentSchemaVersion)
        {
            switch (document.SchemaVersion)
            {
                case 1:
                    if (IsStandardMap(document))
                        ApplyStandardGeometry(document);
                    document.SchemaVersion = 2;
                    wasMigrated = true;
                    break;
                case 2:
                    ApplyVersion3(document);
                    document.SchemaVersion = 3;
                    wasMigrated = true;
                    break;
                case 3:
                    ApplyVersion4(document);
                    document.SchemaVersion = 4;
                    wasMigrated = true;
                    break;
                case 4:
                    ApplyVersion5(document);
                    document.SchemaVersion = 5;
                    wasMigrated = true;
                    break;
                case 5:
                    ApplyVersion6(document);
                    document.SchemaVersion = 6;
                    wasMigrated = true;
                    break;
                case 6:
                    ApplyVersion7(document);
                    document.SchemaVersion = 7;
                    wasMigrated = true;
                    break;
                case 7:
                    ApplyVersion8(document);
                    document.SchemaVersion = 8;
                    wasMigrated = true;
                    break;
                case 8:
                    ApplyVersion9(document);
                    document.SchemaVersion = 9;
                    wasMigrated = true;
                    break;
                case 9:
                    ApplyVersion10(document);
                    document.SchemaVersion = 10;
                    wasMigrated = true;
                    break;
                case 10:
                    ApplyVersion11(document);
                    document.SchemaVersion = 11;
                    wasMigrated = true;
                    break;
                case 11:
                    ApplyVersion12(document);
                    document.SchemaVersion = 12;
                    wasMigrated = true;
                    break;
                case 12:
                    ApplyVersion13(document);
                    document.SchemaVersion = 13;
                    wasMigrated = true;
                    break;
                default:
                    throw new InvalidDataException($"Неизвестный шаг миграции RouteMap schemaVersion={document.SchemaVersion}.");
            }
        }

        return new RouteMapConfigurationMigrationResult(document, wasMigrated);
    }

    private static bool IsStandardMap(RouteMapConfigurationDocument document)
    {
        var chain = document.Chains.SingleOrDefault(x => x.Id == "chain.bucket_route");
        return chain is not null &&
               chain.NodeIds.SequenceEqual(StandardNodeIds) &&
               chain.SegmentIds.SequenceEqual(StandardSegmentIds) &&
               StandardNodeIds.All(id => document.Nodes.Any(x => x.Id == id)) &&
               StandardSegmentIds.All(id => document.Segments.Any(x => x.Id == id));
    }

    private static void ApplyStandardGeometry(RouteMapConfigurationDocument document)
    {
        SetNode(document, "dead_end_lower", 240, 500);
        SetNode(document, "bsu_1", 240, 400);
        SetNode(document, "bsu_2", 240, 300);
        SetNode(document, "concrete_bucket", 550, 100);
        SetNode(document, "dead_end_upper", 830, 100);

        var elbow = document.Segments.Single(x => x.Id == "bsu2_to_bucket");
        elbow.Kind = RouteSegmentKind.RoundedElbow90;
        elbow.ArcRadius = 150;
        elbow.ElbowOrder = RouteElbowOrder.VerticalThenHorizontal;

        var chain = document.Chains.Single(x => x.Id == "chain.bucket_route");
        chain.X = 240;
        chain.Y = 300;
    }

    private static void SetNode(
        RouteMapConfigurationDocument document,
        string id,
        double x,
        double y)
    {
        var node = document.Nodes.Single(item => item.Id == id);
        node.X = x;
        node.Y = y;
        node.LabelOffsetX = 0;
        node.LabelOffsetY = 6;
    }

    private static void ApplyVersion3(RouteMapConfigurationDocument document)
    {
        if (IsStandardMap(document))
        {
            SetRightLabel(document, "dead_end_lower");
            SetRightLabel(document, "bsu_1");
            SetRightLabel(document, "bsu_2");
            SetBelowLabel(document, "concrete_bucket");
            SetBelowLabel(document, "dead_end_upper");
        }

        foreach (var segment in document.Segments)
        {
            segment.Style.EndpointGap = 6;
            segment.Style.LineCap = RouteLineCap.Round;
            var activeBinding = segment.Bindings.FirstOrDefault(x => x.Role == SignalBindingRole.ActiveRoute);
            if (activeBinding is not null)
            {
                activeBinding.Direction = SignalBindingDirection.Read;
                activeBinding.ValueType = SignalValueType.Bool;
                continue;
            }

            segment.Bindings.Add(new SignalBindingConfiguration
            {
                Role = SignalBindingRole.ActiveRoute,
                SignalId = $"route.{segment.Id}.active",
                Direction = SignalBindingDirection.Read,
                ValueType = SignalValueType.Bool,
            });
        }
    }

    private static void SetRightLabel(RouteMapConfigurationDocument document, string id)
    {
        var node = document.Nodes.Single(x => x.Id == id);
        node.LabelPlacement = RouteNodeLabelPlacement.Right;
        node.LabelOffsetX = 10;
        node.LabelOffsetY = 0;
    }

    private static void SetBelowLabel(RouteMapConfigurationDocument document, string id)
    {
        var node = document.Nodes.Single(x => x.Id == id);
        node.LabelPlacement = RouteNodeLabelPlacement.Below;
        node.LabelOffsetX = 0;
        node.LabelOffsetY = 6;
    }

    private static void ApplyVersion4(RouteMapConfigurationDocument document)
    {
        document.TopBar ??= RouteTopBarConfiguration.CreateDefault();
        EnsureTopBarButton(document.TopBar.Automatic, "АВТОМАТ", SignalBindingRole.AutomaticModeCommand, "system.mode.automatic");
        EnsureTopBarButton(document.TopBar.Manual, "РУЧНОЙ", SignalBindingRole.ManualModeCommand, "system.mode.manual");
        EnsureTopBarButton(document.TopBar.Emergency, "АВАРИЯ", SignalBindingRole.EmergencyCommand, "system.emergency");

        foreach (var node in document.Nodes)
        {
            node.Style.ActiveOutlineColor = "#00A6A6";
            node.Style.ActiveOutlineThickness = 3;
            EnsureBinding(node.Bindings, SignalBindingRole.ActiveRoute, $"route.node.{node.Id}.active", SignalBindingDirection.Read);

            if (node.MenuKind is RouteNodeMenuKind.SendOnly or RouteNodeMenuKind.SendAndReturn)
                EnsureBinding(node.Bindings, SignalBindingRole.TargetCommand, $"route.node.{node.Id}.target", SignalBindingDirection.ReadWrite);
            if (node.MenuKind == RouteNodeMenuKind.SendAndReturn)
                EnsureBinding(node.Bindings, SignalBindingRole.LoaderCommand, $"route.node.{node.Id}.loader", SignalBindingDirection.ReadWrite);
        }
    }

    private static void ApplyVersion5(RouteMapConfigurationDocument document)
    {
        document.TopBar ??= RouteTopBarConfiguration.CreateDefault();
        document.TopBar.Emergency ??= RouteTopBarEmergencyButtonConfiguration.Create(
            "АВАРИЯ",
            SignalBindingRole.EmergencyCommand,
            "system.emergency",
            normalBackground: "#D87868",
            checkedBackground: "#C83F30");
        document.TopBar.Emergency.ButtonKind = RouteCommandButtonKind.Toggle;

        foreach (var card in document.Cards)
        {
            card.StartButtonKind = RouteCommandButtonKind.Toggle;
            card.StopButtonKind = RouteCommandButtonKind.Toggle;
        }
    }

    private static void ApplyVersion6(RouteMapConfigurationDocument document)
    {
        document.TopBar ??= RouteTopBarConfiguration.CreateDefault();
        document.TopBar.Automatic ??= RouteTopBarButtonConfiguration.Create("РђР’РўРћРњРђРў", SignalBindingRole.AutomaticModeCommand, "system.mode.automatic");
        document.TopBar.Manual ??= RouteTopBarButtonConfiguration.Create("Р РЈР§РќРћР™", SignalBindingRole.ManualModeCommand, "system.mode.manual");
        document.TopBar.Emergency ??= RouteTopBarEmergencyButtonConfiguration.Create(
            "РђР’РђР РРЇ",
            SignalBindingRole.EmergencyCommand,
            "system.emergency",
            normalBackground: "#D87868",
            checkedBackground: "#C83F30");
        EnsureBinding(document.TopBar.Automatic.Bindings, SignalBindingRole.AutomaticModeOffFeedback, "system.mode.automatic.off", SignalBindingDirection.Read);
        EnsureBinding(document.TopBar.Manual.Bindings, SignalBindingRole.ManualModeOffFeedback, "system.mode.manual.off", SignalBindingDirection.Read);
        EnsureBinding(document.TopBar.Emergency.Bindings, SignalBindingRole.EmergencyOffFeedback, "system.emergency.off", SignalBindingDirection.Read);

        foreach (var node in document.Nodes)
        {
            if (node.MenuKind is RouteNodeMenuKind.SendOnly or RouteNodeMenuKind.SendAndReturn)
                EnsureBinding(node.Bindings, SignalBindingRole.TargetOffFeedback, $"route.node.{node.Id}.target.off", SignalBindingDirection.Read);
            if (node.MenuKind == RouteNodeMenuKind.SendAndReturn)
                EnsureBinding(node.Bindings, SignalBindingRole.LoaderOffFeedback, $"route.node.{node.Id}.loader.off", SignalBindingDirection.Read);
        }

        foreach (var card in document.Cards)
        {
            NormalizeOptionalOffFeedback(card.Bindings, SignalBindingRole.StartOffFeedback);
            NormalizeOptionalOffFeedback(card.Bindings, SignalBindingRole.StopOffFeedback);
        }
    }

    private static void ApplyVersion7(RouteMapConfigurationDocument document)
    {
        foreach (var node in document.Nodes)
            RemoveBindings(node.Bindings, SignalBindingRole.TargetOffFeedback, SignalBindingRole.LoaderOffFeedback);

        document.TopBar ??= RouteTopBarConfiguration.CreateDefault();
        document.TopBar.Automatic ??= RouteTopBarButtonConfiguration.Create("АВТОМАТ", SignalBindingRole.AutomaticModeCommand, "system.mode.automatic");
        document.TopBar.Manual ??= RouteTopBarButtonConfiguration.Create("РУЧНОЙ", SignalBindingRole.ManualModeCommand, "system.mode.manual");
        document.TopBar.Emergency ??= RouteTopBarEmergencyButtonConfiguration.Create(
            "АВАРИЯ",
            SignalBindingRole.EmergencyCommand,
            "system.emergency",
            normalBackground: "#D87868",
            checkedBackground: "#C83F30");
        RemoveBindings(document.TopBar.Automatic.Bindings, SignalBindingRole.AutomaticModeOffFeedback);
        RemoveBindings(document.TopBar.Manual.Bindings, SignalBindingRole.ManualModeOffFeedback);

        document.TopBar.Emergency.OffFeedbackEnabled =
            document.TopBar.Emergency.ButtonKind == RouteCommandButtonKind.Toggle;
        if (document.TopBar.Emergency.OffFeedbackEnabled)
            EnsureBinding(document.TopBar.Emergency.Bindings, SignalBindingRole.EmergencyOffFeedback, "system.emergency.off", SignalBindingDirection.Read);
        else
            RemoveBindings(document.TopBar.Emergency.Bindings, SignalBindingRole.EmergencyOffFeedback);

        foreach (var card in document.Cards)
        {
            NormalizeOptionalOffFeedback(card.Bindings, SignalBindingRole.StartOffFeedback);
            NormalizeOptionalOffFeedback(card.Bindings, SignalBindingRole.StopOffFeedback);
            card.StartOffFeedbackEnabled = HasBinding(card.Bindings, SignalBindingRole.StartOffFeedback);
            card.StopOffFeedbackEnabled = HasBinding(card.Bindings, SignalBindingRole.StopOffFeedback);
        }
    }

    private static void ApplyVersion8(RouteMapConfigurationDocument document)
    {
        foreach (var node in document.Nodes)
            RemoveBindings(node.Bindings, SignalBindingRole.TargetOffFeedback, SignalBindingRole.LoaderOffFeedback);

        document.TopBar ??= RouteTopBarConfiguration.CreateDefault();
        document.TopBar.Automatic ??= RouteTopBarButtonConfiguration.Create("РђР’РўРћРњРђРў", SignalBindingRole.AutomaticModeCommand, "system.mode.automatic");
        document.TopBar.Manual ??= RouteTopBarButtonConfiguration.Create("Р РЈР§РќРћР™", SignalBindingRole.ManualModeCommand, "system.mode.manual");
        document.TopBar.Emergency ??= RouteTopBarEmergencyButtonConfiguration.Create(
            "РђР’РђР РРЇ",
            SignalBindingRole.EmergencyCommand,
            "system.emergency",
            normalBackground: "#D87868",
            checkedBackground: "#C83F30");
        RemoveBindings(document.TopBar.Automatic.Bindings, SignalBindingRole.AutomaticModeOffFeedback);
        RemoveBindings(document.TopBar.Manual.Bindings, SignalBindingRole.ManualModeOffFeedback);
        document.TopBar.Emergency.ButtonKind = RouteCommandButtonKind.Toggle;
        document.TopBar.Emergency.OffFeedbackEnabled = false;
        RemoveBindings(document.TopBar.Emergency.Bindings, SignalBindingRole.EmergencyOffFeedback);

        foreach (var card in document.Cards)
        {
            card.StartButtonKind = RouteCommandButtonKind.Toggle;
            card.StopButtonKind = RouteCommandButtonKind.Toggle;
            NormalizeOptionalOffFeedback(card.Bindings, SignalBindingRole.StartOffFeedback);
            NormalizeOptionalOffFeedback(card.Bindings, SignalBindingRole.StopOffFeedback);
            card.StartOffFeedbackEnabled = HasBinding(card.Bindings, SignalBindingRole.StartOffFeedback);
            card.StopOffFeedbackEnabled = HasBinding(card.Bindings, SignalBindingRole.StopOffFeedback);
        }
    }

    private static void ApplyVersion9(RouteMapConfigurationDocument document)
    {
        foreach (var node in document.Nodes)
            RemoveBindings(node.Bindings, SignalBindingRole.State);

        foreach (var segment in document.Segments)
            RemoveBindings(segment.Bindings, SignalBindingRole.State);

        foreach (var card in document.Cards)
        {
            RemoveBindings(card.Bindings, SignalBindingRole.State);
            if (string.IsNullOrWhiteSpace(card.StatusText) ||
                string.Equals(card.StatusText, "Ожидание", StringComparison.Ordinal))
            {
                card.StatusText = "Выключено";
            }
        }
    }

    private static void ApplyVersion10(RouteMapConfigurationDocument document)
    {
        document.TopBar ??= RouteTopBarConfiguration.CreateDefault();
        ApplyButtonStateDefaults(document.TopBar.Automatic);
        ApplyButtonStateDefaults(document.TopBar.Manual);
        ApplyEmergencyDefaults(document.TopBar.Emergency);

        foreach (var card in document.Cards)
            ApplyCardButtonStateDefaults(card.Style);

        RouteSegmentActiveFragmentSynchronizer.Ensure(document);
    }

    private static void ApplyVersion11(RouteMapConfigurationDocument document)
    {
        foreach (var card in document.Cards)
        {
            card.Parameters ??= [];
            foreach (var parameter in card.Parameters)
                parameter.Role = SignalBindingRole.EquipmentParameter;
        }
    }

    private static void ApplyVersion12(RouteMapConfigurationDocument document)
    {
        if (string.Equals(document.Map.Palette.Disabled, "#D8DCDF", StringComparison.OrdinalIgnoreCase))
            document.Map.Palette.Disabled = "#3F474D";

        foreach (var card in document.Cards)
        {
            EnsureBinding(
                card.Bindings,
                SignalBindingRole.UncheckedCommand,
                $"{card.Id}.selector.off",
                SignalBindingDirection.ReadWrite);
            EnsureBinding(
                card.Bindings,
                SignalBindingRole.CheckedCommand,
                $"{card.Id}.selector.on",
                SignalBindingDirection.ReadWrite);
        }
    }

    private static void ApplyVersion13(RouteMapConfigurationDocument document)
    {
        document.TopBar ??= RouteTopBarConfiguration.CreateDefault();
        document.TopBar.Reset ??= RouteTopBarButtonConfiguration.Create(
            "СБРОС",
            SignalBindingRole.ResetCommand,
            "system.reset",
            normalBackground: "#F2C94C",
            checkedBackground: "#B7791F",
            pressedBackground: "#D6A800",
            normalForeground: "#101820");
        EnsureTopBarButton(document.TopBar.Reset, "СБРОС", SignalBindingRole.ResetCommand, "system.reset");
        ApplyResetDefaults(document.TopBar.Reset);
    }

    private static void ApplyButtonStateDefaults(RouteTopBarButtonConfiguration button)
    {
        if (string.IsNullOrWhiteSpace(button.PressedBackground))
            button.PressedBackground = "#949595";
        if (string.IsNullOrWhiteSpace(button.PressedForeground))
            button.PressedForeground = "#FFFFFF";
    }

    private static void ApplyEmergencyDefaults(RouteTopBarEmergencyButtonConfiguration emergency)
    {
        ApplyButtonStateDefaults(emergency);
        if (IsDefaultEmergencyNormal(emergency.NormalBackground))
            emergency.NormalBackground = "#D95D4E";
        if (IsDefaultEmergencyChecked(emergency.CheckedBackground))
            emergency.CheckedBackground = "#9E2F25";
        if (string.IsNullOrWhiteSpace(emergency.NormalForeground))
            emergency.NormalForeground = "#FFFFFF";
        if (string.IsNullOrWhiteSpace(emergency.CheckedForeground))
            emergency.CheckedForeground = "#FFFFFF";
        emergency.PressedBackground = "#949595";
        if (string.IsNullOrWhiteSpace(emergency.PressedForeground))
            emergency.PressedForeground = "#FFFFFF";
    }

    private static void ApplyResetDefaults(RouteTopBarButtonConfiguration reset)
    {
        if (string.IsNullOrWhiteSpace(reset.NormalBackground) ||
            string.Equals(reset.NormalBackground, "#ECEFF1", StringComparison.OrdinalIgnoreCase))
            reset.NormalBackground = "#F2C94C";
        if (string.IsNullOrWhiteSpace(reset.PressedBackground) ||
            string.Equals(reset.PressedBackground, "#949595", StringComparison.OrdinalIgnoreCase))
            reset.PressedBackground = "#D6A800";
        if (string.IsNullOrWhiteSpace(reset.CheckedBackground) ||
            string.Equals(reset.CheckedBackground, "#3378D6", StringComparison.OrdinalIgnoreCase))
            reset.CheckedBackground = "#B7791F";
        if (string.IsNullOrWhiteSpace(reset.NormalForeground) ||
            string.Equals(reset.NormalForeground, "#59636E", StringComparison.OrdinalIgnoreCase))
            reset.NormalForeground = "#101820";
        if (string.IsNullOrWhiteSpace(reset.PressedForeground))
            reset.PressedForeground = "#FFFFFF";
        if (string.IsNullOrWhiteSpace(reset.CheckedForeground))
            reset.CheckedForeground = "#FFFFFF";
    }

    private static bool IsDefaultEmergencyNormal(string color) =>
        string.Equals(color, "#D87868", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(color, "#D95D4E", StringComparison.OrdinalIgnoreCase);

    private static bool IsDefaultEmergencyChecked(string color) =>
        string.Equals(color, "#C83F30", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(color, "#9E2F25", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(color, "#0078D4", StringComparison.OrdinalIgnoreCase);

    private static void ApplyCardButtonStateDefaults(EquipmentCardStyleConfiguration style)
    {
        if (string.IsNullOrWhiteSpace(style.StartColor))
            style.StartColor = "#D0D0D0";
        if (string.IsNullOrWhiteSpace(style.StartPressedColor))
            style.StartPressedColor = "#949595";
        if (string.IsNullOrWhiteSpace(style.StartCheckedColor) ||
            string.Equals(style.StartCheckedColor, "#0078D4", StringComparison.OrdinalIgnoreCase))
            style.StartCheckedColor = "#3A9D5D";
        if (string.IsNullOrWhiteSpace(style.StartForegroundColor))
            style.StartForegroundColor = "#101820";
        if (string.IsNullOrWhiteSpace(style.StartPressedForegroundColor))
            style.StartPressedForegroundColor = "#101820";
        if (string.IsNullOrWhiteSpace(style.StartCheckedForegroundColor))
            style.StartCheckedForegroundColor = "#FFFFFF";

        if (string.IsNullOrWhiteSpace(style.StopColor))
            style.StopColor = "#D95D4E";
        if (string.IsNullOrWhiteSpace(style.StopPressedColor))
            style.StopPressedColor = "#949595";
        if (string.IsNullOrWhiteSpace(style.StopCheckedColor) ||
            string.Equals(style.StopCheckedColor, "#0078D4", StringComparison.OrdinalIgnoreCase))
            style.StopCheckedColor = "#9E2F25";
        if (string.IsNullOrWhiteSpace(style.StopForegroundColor))
            style.StopForegroundColor = "#FFFFFF";
        if (string.IsNullOrWhiteSpace(style.StopPressedForegroundColor))
            style.StopPressedForegroundColor = "#FFFFFF";
        if (string.IsNullOrWhiteSpace(style.StopCheckedForegroundColor))
            style.StopCheckedForegroundColor = "#FFFFFF";
    }

    private static void EnsureTopBarButton(
        RouteTopBarButtonConfiguration button,
        string defaultText,
        SignalBindingRole role,
        string signalId)
    {
        if (string.IsNullOrWhiteSpace(button.Text))
            button.Text = defaultText;
        EnsureBinding(button.Bindings, role, signalId, SignalBindingDirection.ReadWrite);
    }

    private static void EnsureBinding(
        ICollection<SignalBindingConfiguration> bindings,
        SignalBindingRole role,
        string signalId,
        SignalBindingDirection direction)
    {
        var binding = bindings.FirstOrDefault(x => x.Role == role);
        if (binding is null)
        {
            bindings.Add(new SignalBindingConfiguration
            {
                Role = role,
                SignalId = signalId,
                Direction = direction,
                ValueType = SignalValueType.Bool,
            });
            return;
        }

        binding.Direction = direction;
        binding.ValueType = SignalValueType.Bool;
    }

    private static void NormalizeOptionalOffFeedback(
        ICollection<SignalBindingConfiguration> bindings,
        SignalBindingRole role)
    {
        foreach (var binding in bindings.Where(x => x.Role == role))
        {
            binding.Direction = SignalBindingDirection.Read;
            binding.ValueType = SignalValueType.Bool;
        }
    }

    private static bool HasBinding(
        IEnumerable<SignalBindingConfiguration> bindings,
        SignalBindingRole role) =>
        bindings.Any(x => x.Role == role);

    private static void RemoveBindings(
        ICollection<SignalBindingConfiguration> bindings,
        params SignalBindingRole[] roles)
    {
        var roleSet = roles.ToHashSet();
        foreach (var binding in bindings.Where(x => roleSet.Contains(x.Role)).ToArray())
            bindings.Remove(binding);
    }
}

internal static class RouteSegmentActiveFragmentSynchronizer
{
    public static void Ensure(RouteMapConfigurationDocument document)
    {
        foreach (var segment in document.Segments)
            Ensure(document, segment);
    }

    public static void Ensure(RouteMapConfigurationDocument document, RouteSegmentConfiguration segment)
    {
        var fragmentCount = CalculateFragmentCount(document, segment);
        if (fragmentCount <= 1)
        {
            segment.ActiveFragments.Clear();
            return;
        }

        var byIndex = segment.ActiveFragments
            .GroupBy(fragment => fragment.Index)
            .ToDictionary(group => group.Key, group => group.First());
        var next = new List<RouteSegmentActiveFragmentConfiguration>();
        for (var index = 1; index <= fragmentCount; index++)
        {
            if (!byIndex.TryGetValue(index, out var fragment))
            {
                fragment = new RouteSegmentActiveFragmentConfiguration { Index = index };
            }

            fragment.Index = index;
            NormalizeBinding(fragment.Binding, segment.Id, index);
            next.Add(fragment);
        }

        segment.ActiveFragments.Clear();
        foreach (var fragment in next)
            segment.ActiveFragments.Add(fragment);
    }

    public static int CalculateFragmentCount(RouteMapConfigurationDocument document, RouteSegmentConfiguration segment)
    {
        var from = document.Nodes.FirstOrDefault(node => node.Id == segment.FromNodeId);
        var to = document.Nodes.FirstOrDefault(node => node.Id == segment.ToNodeId);
        if (from is null || to is null)
            return 0;

        var display = new RouteMapDisplaySettings
        {
            FragmentLength = document.Map.FragmentLength,
            FragmentGap = document.Map.FragmentGap,
        };

        return RouteSegmentGeometry.CalculateLogicalDrawableRanges(
            ToModel(segment),
            ToModel(from),
            ToModel(to),
            display).Count;
    }

    private static void NormalizeBinding(SignalBindingConfiguration binding, string segmentId, int index)
    {
        binding.Role = SignalBindingRole.ActiveRouteFragment;
        if (string.IsNullOrWhiteSpace(binding.SignalId))
            binding.SignalId = DefaultSignalId(segmentId, index);
        binding.Direction = SignalBindingDirection.Read;
        binding.ValueType = SignalValueType.Bool;
    }

    private static string DefaultSignalId(string segmentId, int index) =>
        $"route.{segmentId}.fragment_{index}.active";

    private static RouteNode ToModel(RouteNodeConfiguration node) => new(
        node.Id,
        node.Title,
        node.X,
        node.Y,
        node.Kind,
        node.State,
        [],
        node.LabelOffsetX,
        node.LabelOffsetY,
        node.LabelPlacement,
        node.IsLoader,
        node.IsTarget,
        node.MenuKind,
        node.IsVisible,
        new RouteNodeStyle
        {
            Radius = node.Style.Radius,
            InnerRadiusRatio = node.Style.InnerRadiusRatio,
            BorderThickness = node.Style.BorderThickness,
            FillColor = node.Style.FillColor,
            BorderColor = node.Style.BorderColor,
            InnerColor = node.Style.InnerColor,
            LabelColor = node.Style.LabelColor,
            LabelFontSize = node.Style.LabelFontSize,
            ActiveOutlineColor = node.Style.ActiveOutlineColor,
            ActiveOutlineThickness = node.Style.ActiveOutlineThickness,
        });

    private static RouteSegment ToModel(RouteSegmentConfiguration segment) => new(
        segment.Id,
        segment.FromNodeId,
        segment.ToNodeId,
        segment.State,
        segment.IsDirectional,
        [],
        segment.Kind,
        segment.ArcRadius,
        segment.ElbowOrder,
        segment.Title,
        segment.LabelOffsetX,
        segment.LabelOffsetY,
        segment.IsVisible,
        new RouteSegmentStyle
        {
            NormalColor = segment.Style.NormalColor,
            ActiveColor = segment.Style.ActiveColor,
            Thickness = segment.Style.Thickness,
            ActiveThickness = segment.Style.ActiveThickness,
            FragmentLength = segment.Style.FragmentLength,
            FragmentGap = segment.Style.FragmentGap,
            EndpointGap = segment.Style.EndpointGap,
            LineCap = segment.Style.LineCap,
            LabelColor = segment.Style.LabelColor,
            LabelFontSize = segment.Style.LabelFontSize,
        });
}
