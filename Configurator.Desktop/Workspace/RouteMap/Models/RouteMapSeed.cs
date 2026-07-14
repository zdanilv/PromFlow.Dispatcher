using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Controls;

namespace Configurator.Desktop.Workspace.RouteMap.Models;

public static class RouteMapSeed
{
    public static RouteMapDefinition Create()
    {
        const string mainChainId = "chain.bucket_route";
        const double mainChainX = 240;
        const double mainChainY = 300;
        const double attachedCardRightOffset = 0;

        var nodes = new[]
        {
            Node("dead_end_lower", "ТУПИК", 240, 500, RouteNodeKind.ServicePoint, 10, 0, RouteNodeLabelPlacement.Right, menuKind: RouteNodeMenuKind.None),
            Node("bsu_1", "БСУ 1", 240, 400, RouteNodeKind.Mixer, 10, 0, RouteNodeLabelPlacement.Right, isLoader: true, menuKind: RouteNodeMenuKind.SendAndReturn),
            Node("bsu_2", "БСУ 2", 240, 300, RouteNodeKind.Mixer, 10, 0, RouteNodeLabelPlacement.Right, menuKind: RouteNodeMenuKind.SendAndReturn),
            Node("concrete_bucket", "БЕТОНОУКЛ.", 550, 100, RouteNodeKind.ServicePoint, 0, 6, isTarget: true, menuKind: RouteNodeMenuKind.SendOnly),
            Node("dead_end_upper", "ТУПИК", 830, 100, RouteNodeKind.ServicePoint, 0, 6, menuKind: RouteNodeMenuKind.None),
        };

        var segments = AddActiveFragments([
            Segment("lower_dead_end_to_bsu1", "dead_end_lower", "bsu_1"),
            ActiveSegment("active_bsu1_bsu2", "bsu_1", "bsu_2"),
            Segment(
                "bsu2_to_bucket",
                "bsu_2",
                "concrete_bucket",
                kind: RouteSegmentKind.RoundedElbow90,
                arcRadius: 150,
                elbowOrder: RouteElbowOrder.VerticalThenHorizontal,
                title: "ПОВОРОТ",
                labelOffsetX: 20,
                labelOffsetY: 15),
            Segment("bucket_to_upper_dead_end", "concrete_bucket", "dead_end_upper"),
        ], nodes, new RouteMapDisplaySettings());

        var chains = new[]
        {
            new RouteChain(
                mainChainId,
                mainChainX,
                mainChainY,
                nodes.Select(x => x.Id).ToArray(),
                segments.Select(x => x.Id).ToArray(),
                "equip.bucket",
                attachedCardRightOffset,
                RouteCardVerticalAnchor.Node("concrete_bucket")),
        };

        var vehicles = Array.Empty<RouteVehicle>();

        var mapEquipment = new[]
        {
            Equipment("equip.bucket", "Кюбель Л.К.", canStart: true, canStop: true),
        };

        var requests = new[]
        {
            new RequestItem(1, "Бетон М300", "1.5 м³", "Бункер 3", "Тел. 3"),
            new RequestItem(2, "Раствор П4", "1.0 м³", "Бункер 2", "Кюбель"),
        };

        var templates = new[]
        {
            new RequestTemplateItem("template.m300", "М300 → Тел. 3", "Бетон М300", "1.5 м³"),
            new RequestTemplateItem("template.p4", "П4 → Кюбель", "Раствор П4", "1.0 м³"),
        };

        return new RouteMapDefinition(
            LogicalWidth: 1200,
            LogicalHeight: 800,
            chains,
            nodes,
            segments,
            vehicles,
            mapEquipment,
            requests,
            templates,
            TopBar: CreateTopBar());
    }

    private static RouteNode Node(
        string id,
        string title,
        double x,
        double y,
        RouteNodeKind kind,
        double labelOffsetX,
        double labelOffsetY,
        RouteNodeLabelPlacement labelPlacement = RouteNodeLabelPlacement.Below,
        bool isLoader = false,
        bool isTarget = false,
        RouteNodeMenuKind menuKind = RouteNodeMenuKind.None)
    {
        return new RouteNode(
            id,
            title,
            x,
            y,
            kind,
            RouteObjectState.Idle,
            NodeBindings(id, menuKind),
            labelOffsetX,
            labelOffsetY,
            labelPlacement,
            isLoader,
            isTarget,
            menuKind);
    }

    private static RouteSegment Segment(
        string id,
        string fromNodeId,
        string toNodeId,
        RouteSegmentKind kind = RouteSegmentKind.Straight,
        double arcRadius = 0,
        RouteElbowOrder elbowOrder = RouteElbowOrder.VerticalThenHorizontal,
        string? title = null,
        double labelOffsetX = 0,
        double labelOffsetY = 0)
    {
        return new RouteSegment(
            id,
            fromNodeId,
            toNodeId,
            RouteObjectState.Idle,
            IsDirectional: false,
            SegmentBindings(id),
            kind,
            arcRadius,
            elbowOrder,
            title,
            labelOffsetX,
            labelOffsetY);
    }

    private static RouteSegment ActiveSegment(string id, string fromNodeId, string toNodeId)
    {
        return new RouteSegment(
            id,
            fromNodeId,
            toNodeId,
            RouteObjectState.Idle,
            IsDirectional: true,
            SegmentBindings(id));
    }

    private static EquipmentCommandCard Equipment(string id, string title, bool canStart, bool canStop)
    {
        return new EquipmentCommandCard(
            id,
            title,
            "Выключено",
            RouteObjectState.Idle,
            canStart,
            canStop,
            new[]
            {
                new SignalBinding(SignalBindingRole.Text, $"{id}.text", SignalBindingDirection.Read, SignalValueType.UInt16),
                new SignalBinding(SignalBindingRole.StartCommand, $"{id}.start", SignalBindingDirection.ReadWrite, SignalValueType.Bool),
                new SignalBinding(SignalBindingRole.StopCommand, $"{id}.stop", SignalBindingDirection.ReadWrite, SignalValueType.Bool),
                new SignalBinding(SignalBindingRole.UncheckedCommand, $"{id}.selector.off", SignalBindingDirection.ReadWrite, SignalValueType.Bool),
                new SignalBinding(SignalBindingRole.CheckedCommand, $"{id}.selector.on", SignalBindingDirection.ReadWrite, SignalValueType.Bool),
            });
    }

    private static SignalBinding[] NodeBindings(string id, RouteNodeMenuKind menuKind)
    {
        var bindings = new List<SignalBinding>
        {
            new SignalBinding(SignalBindingRole.Fault, $"{id}.fault", SignalBindingDirection.Read, SignalValueType.Bool),
            new SignalBinding(SignalBindingRole.ActiveRoute, $"route.node.{id}.active", SignalBindingDirection.Read, SignalValueType.Bool),
        };

        if (menuKind is RouteNodeMenuKind.SendOnly or RouteNodeMenuKind.SendAndReturn)
        {
            bindings.Add(new SignalBinding(SignalBindingRole.TargetCommand, $"route.node.{id}.target", SignalBindingDirection.ReadWrite, SignalValueType.Bool));
        }
        if (menuKind == RouteNodeMenuKind.SendAndReturn)
        {
            bindings.Add(new SignalBinding(SignalBindingRole.LoaderCommand, $"route.node.{id}.loader", SignalBindingDirection.ReadWrite, SignalValueType.Bool));
        }

        return bindings.ToArray();
    }

    private static RouteTopBarSettings CreateTopBar() => new(
        new RouteTopBarButtonSettings
        {
            Text = "АВТОМАТ",
            Binding = new SignalBinding(SignalBindingRole.AutomaticModeCommand, "system.mode.automatic", SignalBindingDirection.ReadWrite, SignalValueType.Bool),
        },
        new RouteTopBarButtonSettings
        {
            Text = "РУЧНОЙ",
            Binding = new SignalBinding(SignalBindingRole.ManualModeCommand, "system.mode.manual", SignalBindingDirection.ReadWrite, SignalValueType.Bool),
        },
        new RouteTopBarButtonSettings
        {
            Text = "СБРОС",
            NormalBackground = "#F2C94C",
            PressedBackground = "#D6A800",
            CheckedBackground = "#B7791F",
            NormalForeground = "#101820",
            PressedForeground = "#FFFFFF",
            CheckedForeground = "#FFFFFF",
            Binding = new SignalBinding(SignalBindingRole.ResetCommand, "system.reset", SignalBindingDirection.ReadWrite, SignalValueType.Bool),
        },
        new RouteTopBarButtonSettings
        {
            Text = "АВАРИЯ",
            NormalBackground = "#D95D4E",
            PressedBackground = "#949595",
            CheckedBackground = "#9E2F25",
            NormalForeground = "#FFFFFF",
            PressedForeground = "#FFFFFF",
            CheckedForeground = "#FFFFFF",
            Binding = new SignalBinding(SignalBindingRole.EmergencyCommand, "system.emergency", SignalBindingDirection.ReadWrite, SignalValueType.Bool),
        });

    private static SignalBinding[] SegmentBindings(string id)
    {
        return
        [
            new SignalBinding(SignalBindingRole.Fault, $"{id}.fault", SignalBindingDirection.Read, SignalValueType.Bool),
            new SignalBinding(SignalBindingRole.ActiveRoute, $"route.{id}.active", SignalBindingDirection.Read, SignalValueType.Bool),
        ];
    }

    private static RouteSegment[] AddActiveFragments(
        IReadOnlyList<RouteSegment> segments,
        IReadOnlyList<RouteNode> nodes,
        RouteMapDisplaySettings display)
    {
        var nodesById = nodes.ToDictionary(node => node.Id);
        return segments.Select(segment =>
        {
            if (!nodesById.TryGetValue(segment.FromNodeId, out var from) ||
                !nodesById.TryGetValue(segment.ToNodeId, out var to))
            {
                return segment;
            }

            var ranges = RouteSegmentGeometry.CalculateLogicalDrawableRanges(segment, from, to, display);
            if (ranges.Count <= 1)
                return segment with { ActiveFragments = [] };

            return segment with
            {
                ActiveFragments = Enumerable.Range(1, ranges.Count)
                    .Select(index => new RouteSegmentActiveFragment(
                        index,
                        new SignalBinding(
                            SignalBindingRole.ActiveRouteFragment,
                            $"route.{segment.Id}.fragment_{index}.active",
                            SignalBindingDirection.Read,
                            SignalValueType.Bool)))
                    .ToArray()
            };
        }).ToArray();
    }
}
