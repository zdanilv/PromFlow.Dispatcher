using Avalonia;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class RouteMapSeedTests
{
    [Fact]
    public void Create_contains_only_simplified_route_map_objects()
    {
        var definition = RouteMapSeed.Create();

        Assert.Equal(
            new[]
            {
                "dead_end_lower",
                "bsu_1",
                "bsu_2",
                "concrete_bucket",
                "dead_end_upper",
            },
            definition.Nodes.Select(x => x.Id));

        Assert.Equal(
            new[]
            {
                "lower_dead_end_to_bsu1",
                "active_bsu1_bsu2",
                "bsu2_to_bucket",
                "bucket_to_upper_dead_end",
            },
            definition.Segments.Select(x => x.Id));

        var chain = Assert.Single(definition.Chains);
        Assert.Equal("chain.bucket_route", chain.Id);
        Assert.Equal(240, chain.X);
        Assert.Equal(300, chain.Y);
        Assert.Equal(definition.Nodes.Select(x => x.Id), chain.NodeIds);
        Assert.Equal(definition.Segments.Select(x => x.Id), chain.SegmentIds);
        Assert.Equal("equip.bucket", chain.AttachedEquipmentCardId);
        Assert.Equal(0, chain.AttachedCardRightOffset);
        Assert.Equal(RouteCardVerticalAnchorKind.Node, chain.AttachedCardVerticalAnchor.Kind);
        Assert.Equal("concrete_bucket", chain.AttachedCardVerticalAnchor.NodeId);

        Assert.Equal(1200, definition.LogicalWidth);
        Assert.Equal(800, definition.LogicalHeight);

        Assert.Empty(definition.Vehicles);

        var mapEquipment = Assert.Single(definition.MapEquipment);
        Assert.Equal("equip.bucket", mapEquipment.Id);
        Assert.Equal("Кюбель Л.К.", mapEquipment.Title);
        Assert.Equal("Выключено", mapEquipment.StatusText);
        Assert.True(mapEquipment.CanStart);
        Assert.True(mapEquipment.CanStop);

        var bindingsByRole = mapEquipment.Bindings.ToDictionary(x => x.Role);
        Assert.Equal(SignalBindingDirection.Read, bindingsByRole[SignalBindingRole.Text].Direction);
        Assert.Equal(Configurator.Application.Services.Signals.SignalValueType.UInt16, bindingsByRole[SignalBindingRole.Text].ValueType);
        Assert.Equal(SignalBindingDirection.ReadWrite, bindingsByRole[SignalBindingRole.StartCommand].Direction);
        Assert.Equal(SignalBindingDirection.ReadWrite, bindingsByRole[SignalBindingRole.StopCommand].Direction);
        Assert.DoesNotContain(mapEquipment.Bindings, x => x.Role == SignalBindingRole.State);
        Assert.DoesNotContain(mapEquipment.Bindings, x => x.Role == SignalBindingRole.StartOffFeedback);
        Assert.DoesNotContain(mapEquipment.Bindings, x => x.Role == SignalBindingRole.StopOffFeedback);

        var nodesById = definition.Nodes.ToDictionary(x => x.Id);
        Assert.Equal((240d, 500d), (nodesById["dead_end_lower"].X, nodesById["dead_end_lower"].Y));
        Assert.Equal((240d, 400d), (nodesById["bsu_1"].X, nodesById["bsu_1"].Y));
        Assert.Equal((240d, 300d), (nodesById["bsu_2"].X, nodesById["bsu_2"].Y));
        Assert.Equal((550d, 100d), (nodesById["concrete_bucket"].X, nodesById["concrete_bucket"].Y));
        Assert.Equal((830d, 100d), (nodesById["dead_end_upper"].X, nodesById["dead_end_upper"].Y));
        Assert.Equal(100, nodesById["dead_end_lower"].Y - nodesById["bsu_1"].Y);
        Assert.Equal(100, nodesById["bsu_1"].Y - nodesById["bsu_2"].Y);
        foreach (var nodeId in new[] { "dead_end_lower", "bsu_1", "bsu_2" })
        {
            var node = nodesById[nodeId];
            Assert.Equal(RouteNodeLabelPlacement.Right, node.LabelPlacement);
            Assert.Equal(10, node.LabelOffsetX);
            Assert.Equal(0, node.LabelOffsetY);
        }
        foreach (var nodeId in new[] { "concrete_bucket", "dead_end_upper" })
        {
            var node = nodesById[nodeId];
            Assert.Equal(RouteNodeLabelPlacement.Below, node.LabelPlacement);
            Assert.Equal(0, node.LabelOffsetX);
            Assert.Equal(6, node.LabelOffsetY);
        }
        Assert.True(nodesById["bsu_1"].IsLoader);
        Assert.False(nodesById["bsu_1"].IsTarget);
        Assert.True(nodesById["concrete_bucket"].IsTarget);
        Assert.False(nodesById["concrete_bucket"].IsLoader);
        Assert.DoesNotContain(definition.Nodes, x => x.Id == "turn");

        foreach (var nodeId in new[] { "bsu_1", "bsu_2" })
            Assert.Equal(RouteNodeMenuKind.SendAndReturn, nodesById[nodeId].MenuKind);

        Assert.Equal(RouteNodeMenuKind.SendOnly, nodesById["concrete_bucket"].MenuKind);
        Assert.DoesNotContain(nodesById["bsu_1"].Bindings, x => x.Role == SignalBindingRole.TargetOffFeedback);
        Assert.DoesNotContain(nodesById["bsu_1"].Bindings, x => x.Role == SignalBindingRole.LoaderOffFeedback);
        Assert.DoesNotContain(nodesById["concrete_bucket"].Bindings, x => x.Role == SignalBindingRole.TargetOffFeedback);
        Assert.Equal(RouteNodeMenuKind.None, nodesById["dead_end_lower"].MenuKind);
        Assert.Equal(RouteNodeMenuKind.None, nodesById["dead_end_upper"].MenuKind);
        Assert.NotNull(definition.TopBar);
        Assert.Equal("system.mode.automatic", definition.TopBar.Automatic.Binding.SignalId);
        Assert.Null(definition.TopBar.Automatic.OffFeedbackBinding);
        Assert.Equal("system.mode.manual", definition.TopBar.Manual.Binding.SignalId);
        Assert.Null(definition.TopBar.Manual.OffFeedbackBinding);
        Assert.Equal("system.emergency", definition.TopBar.Emergency.Binding.SignalId);
        Assert.False(definition.TopBar.Emergency.OffFeedbackEnabled);
        Assert.Null(definition.TopBar.Emergency.OffFeedbackBinding);
        foreach (var node in definition.Nodes)
        {
            Assert.DoesNotContain(node.Bindings, x => x.Role == SignalBindingRole.State);
            var active = Assert.Single(node.Bindings, x => x.Role == SignalBindingRole.ActiveRoute);
            Assert.Equal($"route.node.{node.Id}.active", active.SignalId);
            var style = node.Style ?? new RouteNodeStyle();
            Assert.Equal("#00A6A6", style.ActiveOutlineColor);
            Assert.Equal(3, style.ActiveOutlineThickness);
        }

        var elbow = definition.Segments.Single(x => x.Id == "bsu2_to_bucket");
        Assert.Equal(RouteSegmentKind.RoundedElbow90, elbow.Kind);
        Assert.Equal(RouteElbowOrder.VerticalThenHorizontal, elbow.ElbowOrder);
        Assert.Equal(150, elbow.ArcRadius);
        Assert.Equal("ПОВОРОТ", elbow.Title);
        Assert.All(definition.Segments, segment =>
        {
            Assert.DoesNotContain(segment.Bindings, x => x.Role == SignalBindingRole.State);
            var activeBinding = Assert.Single(segment.Bindings, x => x.Role == SignalBindingRole.ActiveRoute);
            Assert.Equal($"route.{segment.Id}.active", activeBinding.SignalId);
            Assert.Equal(SignalBindingDirection.Read, activeBinding.Direction);
            Assert.Equal(Configurator.Application.Services.Signals.SignalValueType.Bool, activeBinding.ValueType);
            Assert.Equal(6, (segment.Style ?? new RouteSegmentStyle()).EndpointGap);
            Assert.Equal(RouteLineCap.Round, (segment.Style ?? new RouteSegmentStyle()).LineCap);
        });

        var elbowPath = RouteSegmentGeometry.Create(
            elbow,
            new Point(nodesById[elbow.FromNodeId].X, nodesById[elbow.FromNodeId].Y),
            new Point(nodesById[elbow.ToNodeId].X, nodesById[elbow.ToNodeId].Y));
        var elbowArc = Assert.Single(elbowPath.Parts.OfType<RouteArcPathPart>());
        var elbowExit = Assert.IsType<RouteLinePathPart>(elbowPath.Parts[^1]);
        Assert.Equal(390, elbowArc.End.X, precision: 8);
        Assert.Equal(160, elbowExit.Length, precision: 8);

        var upperSegment = definition.Segments.Single(x => x.Id == "bucket_to_upper_dead_end");
        var upperPath = RouteSegmentGeometry.Create(
            upperSegment,
            new Point(nodesById[upperSegment.FromNodeId].X, nodesById[upperSegment.FromNodeId].Y),
            new Point(nodesById[upperSegment.ToNodeId].X, nodesById[upperSegment.ToNodeId].Y));
        Assert.Equal(280, upperPath.Length, precision: 8);
    }
}
