using Configurator.Application.Services.Signals;
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
            EnsureBinding(card.Bindings, SignalBindingRole.StartOffFeedback, $"{card.Id}.start.off", SignalBindingDirection.Read);
            EnsureBinding(card.Bindings, SignalBindingRole.StopOffFeedback, $"{card.Id}.stop.off", SignalBindingDirection.Read);
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
            card.StartOffFeedbackEnabled = card.StartButtonKind == RouteCommandButtonKind.Toggle;
            card.StopOffFeedbackEnabled = card.StopButtonKind == RouteCommandButtonKind.Toggle;
            if (card.StartOffFeedbackEnabled)
                EnsureBinding(card.Bindings, SignalBindingRole.StartOffFeedback, $"{card.Id}.start.off", SignalBindingDirection.Read);
            else
                RemoveBindings(card.Bindings, SignalBindingRole.StartOffFeedback);
            if (card.StopOffFeedbackEnabled)
                EnsureBinding(card.Bindings, SignalBindingRole.StopOffFeedback, $"{card.Id}.stop.off", SignalBindingDirection.Read);
            else
                RemoveBindings(card.Bindings, SignalBindingRole.StopOffFeedback);
        }
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

    private static void RemoveBindings(
        ICollection<SignalBindingConfiguration> bindings,
        params SignalBindingRole[] roles)
    {
        var roleSet = roles.ToHashSet();
        foreach (var binding in bindings.Where(x => roleSet.Contains(x.Role)).ToArray())
            bindings.Remove(binding);
    }
}
