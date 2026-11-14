using Avalonia;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class RouteMapSeedTests
{
    [Theory]
    [InlineData(SignalBindingRole.UnloadCommand, "ВЫГРУЗКА", "system.command.unload")]
    [InlineData(SignalBindingRole.RotationCommand, "ВРАЩЕНИЕ", "system.command.rotate")]
    [InlineData(SignalBindingRole.LoadCommand, "ЗАГРУЗКА", "system.command.load")]
    public void Create_defines_momentary_top_bar_commands(SignalBindingRole role, string text, string signalId)
    {
        var topBar = Assert.IsType<RouteTopBarSettings>(RouteMapSeed.Create().TopBar);
        var button = Assert.Single(new[] { topBar.Unload, topBar.Rotation, topBar.Load }, item => item.Binding.Role == role);

        Assert.Equal(text, button.Text);
        Assert.Equal(RouteCommandButtonKind.Momentary, button.ButtonKind);
        Assert.Equal(new SignalBinding(role, signalId, SignalBindingDirection.ReadWrite, SignalValueType.Bool), button.Binding);
        Assert.Null(button.EnabledBinding);
    }

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
                "wash",
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
                "wash_to_upper_dead_end",
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

        Assert.Equal(760, definition.LogicalWidth);
        Assert.Equal(550, definition.LogicalHeight);

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
        Assert.Equal(SignalBindingDirection.Read, bindingsByRole[SignalBindingRole.PowerFeedback].Direction);
        Assert.Equal(Configurator.Application.Services.Signals.SignalValueType.Bool, bindingsByRole[SignalBindingRole.PowerFeedback].ValueType);
        Assert.Equal("equip.bucket.power", bindingsByRole[SignalBindingRole.PowerFeedback].SignalId);
        Assert.Equal(RouteCommandButtonKind.Momentary, mapEquipment.StartButtonKind);
        Assert.Equal(RouteCommandButtonKind.Toggle, mapEquipment.StopButtonKind);
        Assert.DoesNotContain(mapEquipment.Bindings, x => x.Role is SignalBindingRole.UncheckedCommand or SignalBindingRole.CheckedCommand);
        Assert.DoesNotContain(mapEquipment.Bindings, x => x.Role == SignalBindingRole.State);
        Assert.DoesNotContain(mapEquipment.Bindings, x => x.Role == SignalBindingRole.StartOffFeedback);
        Assert.DoesNotContain(mapEquipment.Bindings, x => x.Role == SignalBindingRole.StopOffFeedback);

        var nodesById = definition.Nodes.ToDictionary(x => x.Id);
        Assert.Equal((240d, 438d), (nodesById["dead_end_lower"].X, nodesById["dead_end_lower"].Y));
        Assert.Equal((240d, 400d), (nodesById["bsu_1"].X, nodesById["bsu_1"].Y));
        Assert.Equal((240d, 300d), (nodesById["bsu_2"].X, nodesById["bsu_2"].Y));
        Assert.Equal((550d, 100d), (nodesById["concrete_bucket"].X, nodesById["concrete_bucket"].Y));
        Assert.Equal((830d, 100d), (nodesById["wash"].X, nodesById["wash"].Y));
        Assert.Equal((872d, 100d), (nodesById["dead_end_upper"].X, nodesById["dead_end_upper"].Y));
        Assert.Equal(100, nodesById["bsu_1"].Y - nodesById["bsu_2"].Y);
        foreach (var nodeId in new[] { "bsu_1", "bsu_2" })
        {
            var node = nodesById[nodeId];
            Assert.Equal(RouteNodeLabelPlacement.Right, node.LabelPlacement);
            Assert.Equal(10, node.LabelOffsetX);
            Assert.Equal(0, node.LabelOffsetY);
        }
        foreach (var nodeId in new[] { "concrete_bucket", "wash" })
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
        foreach (var id in new[] { "dead_end_lower", "dead_end_upper" })
        {
            var anchor = nodesById[id];
            Assert.Equal(RouteNodeKind.TerminalAnchor, anchor.Kind);
            Assert.Equal(RouteNodeMenuKind.None, anchor.MenuKind);
            Assert.False(anchor.IsLoader);
            Assert.False(anchor.IsTarget);
            Assert.Empty(anchor.Bindings);
        }
        var wash = nodesById["wash"];
        Assert.Equal("МОЙКА", wash.Title);
        Assert.Equal(RouteNodeKind.ServicePoint, wash.Kind);
        Assert.Equal(RouteNodeMenuKind.SendOnly, wash.MenuKind);
        Assert.Equal(RouteObjectState.Idle, wash.State);
        Assert.True(wash.IsVisible);
        Assert.False(wash.IsLoader);
        Assert.False(wash.IsTarget);
        Assert.Collection(wash.Bindings,
            binding => Assert.Equal(new SignalBinding(SignalBindingRole.TargetCommand, "route.node.wash.target", SignalBindingDirection.ReadWrite, SignalValueType.Bool), binding),
            binding => Assert.Equal(new SignalBinding(SignalBindingRole.ActiveRoute, "route.node.wash.active", SignalBindingDirection.Read, SignalValueType.Bool), binding));
        Assert.NotNull(definition.TopBar);
        Assert.Equal("system.mode.automatic", definition.TopBar.Automatic.Binding.SignalId);
        Assert.Null(definition.TopBar.Automatic.OffFeedbackBinding);
        Assert.Equal("system.mode.manual", definition.TopBar.Manual.Binding.SignalId);
        Assert.Null(definition.TopBar.Manual.OffFeedbackBinding);
        Assert.Equal("system.emergency", definition.TopBar.Emergency.Binding.SignalId);
        Assert.False(definition.TopBar.Emergency.OffFeedbackEnabled);
        Assert.Null(definition.TopBar.Emergency.OffFeedbackBinding);
        var stations = definition.Nodes.Where(node => node.IsVisible && node.Kind != RouteNodeKind.TerminalAnchor).ToArray();
        Assert.Equal(4, stations.Length);
        foreach (var node in stations)
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
        for (var index = 0; index < definition.Segments.Count; index++)
        {
            Assert.Equal(chain.NodeIds[index], definition.Segments[index].FromNodeId);
            Assert.Equal(chain.NodeIds[index + 1], definition.Segments[index].ToNodeId);
        }
        Assert.All(definition.Segments.Take(4), segment =>
        {
            Assert.DoesNotContain(segment.Bindings, x => x.Role == SignalBindingRole.State);
            var activeBinding = Assert.Single(segment.Bindings, x => x.Role == SignalBindingRole.ActiveRoute);
            Assert.Equal($"route.{segment.Id}.active", activeBinding.SignalId);
            Assert.Equal(SignalBindingDirection.Read, activeBinding.Direction);
            Assert.Equal(Configurator.Application.Services.Signals.SignalValueType.Bool, activeBinding.ValueType);
            Assert.Equal(6, (segment.Style ?? new RouteSegmentStyle()).EndpointGap);
            Assert.Equal(RouteLineCap.Round, (segment.Style ?? new RouteSegmentStyle()).LineCap);
            Assert.Equal($"{segment.Id}.fault", Assert.Single(segment.Bindings, binding => binding.Role == SignalBindingRole.Fault).SignalId);
        });
        var deadEnds = definition.Segments.Where(line => line.Kind == RouteSegmentKind.DeadEnd).ToArray();
        Assert.Collection(deadEnds,
            line =>
            {
                Assert.Equal("lower_dead_end_to_bsu1", line.Id);
                Assert.Equal(RouteTerminalSide.From, line.TerminalSide);
            },
            line =>
            {
                Assert.Equal("wash_to_upper_dead_end", line.Id);
                Assert.Equal(RouteTerminalSide.To, line.TerminalSide);
                Assert.Empty(line.Bindings);
                Assert.Empty(line.ActiveFragments!);
            });
        Assert.Empty(definition.Segments.Single(x => x.Id == "lower_dead_end_to_bsu1").ActiveFragments ?? []);
        Assert.Empty(definition.Segments.Single(x => x.Id == "active_bsu1_bsu2").ActiveFragments ?? []);
        Assert.Equal(
            Enumerable.Range(1, 3).Select(index => $"route.bsu2_to_bucket.fragment_{index}.active"),
            definition.Segments.Single(x => x.Id == "bsu2_to_bucket").ActiveFragments!.Select(fragment => fragment.Binding.SignalId));
        Assert.Equal(
            Enumerable.Range(1, 2).Select(index => $"route.bucket_to_upper_dead_end.fragment_{index}.active"),
            definition.Segments.Single(x => x.Id == "bucket_to_upper_dead_end").ActiveFragments!.Select(fragment => fragment.Binding.SignalId));

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
