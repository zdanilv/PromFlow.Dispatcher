using Avalonia;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class RouteMapHitTesterTests
{
    [Fact]
    public void Seed_wash_is_selectable_and_terminal_ends_are_not()
    {
        var definition = RouteMapSeed.Create();
        var transform = RouteMapViewportLayout.Create(definition, 1142, 749).Transform;
        foreach (var id in new[] { "wash", "dead_end_lower", "dead_end_upper" })
        {
            var node = definition.Nodes.Single(item => item.Id == id);
            var hit = RouteMapHitTester.HitTest(
                definition, RouteMapRuntimeState.Empty, transform.ToViewPoint(node.X, node.Y), transform);
            Assert.Equal(id == "wash" ? new RouteMapHitResult(id, RouteMapHitKind.Node) : null, hit);
        }
    }

    [Fact]
    public void HitTest_ignores_terminal_anchor_and_both_cap_arms_but_still_selects_the_station()
    {
        var station = new RouteNode("station", "Station", 20, 40, RouteNodeKind.ServicePoint, RouteObjectState.Idle, []);
        var anchor = station with { Id = "anchor", X = 100, Kind = RouteNodeKind.TerminalAnchor, MenuKind = RouteNodeMenuKind.SendAndReturn };
        var segment = new RouteSegment("dead-end", station.Id, anchor.Id, RouteObjectState.Idle, false, [], RouteSegmentKind.DeadEnd)
        {
            TerminalSide = RouteTerminalSide.To,
        };
        var definition = new RouteMapDefinition(120, 80, [], [station, anchor], [segment], [], [], [], []);
        var transform = RouteMapTransform.Create(120, 80, 240, 160);

        foreach (var point in new[] { new Point(100, 40), new Point(100, 26), new Point(100, 54) })
            Assert.Null(RouteMapHitTester.HitTest(definition, RouteMapRuntimeState.Empty, transform.ToViewPoint(point.X, point.Y), transform));

        var stationHit = RouteMapHitTester.HitTest(
            definition, RouteMapRuntimeState.Empty, transform.ToViewPoint(station.X, station.Y), transform);
        Assert.Equal(new RouteMapHitResult(station.Id, RouteMapHitKind.Node), stationHit);
    }

    [Fact]
    public void HitTest_selects_node_near_pointer()
    {
        var definition = RouteMapSeed.Create();
        var transform = RouteMapTransform.Create(
            definition.LogicalWidth,
            definition.LogicalHeight,
            1120,
            700,
            padding: 0);

        var bsu1 = definition.Nodes.Single(x => x.Id == "bsu_1");
        var hit = RouteMapHitTester.HitTest(
            definition,
            RouteMapRuntimeState.Empty,
            transform.ToViewPoint(bsu1.X + 4, bsu1.Y),
            transform);

        Assert.NotNull(hit);
        Assert.Equal("bsu_1", hit.ObjectId);
        Assert.Equal(RouteMapHitKind.Node, hit.Kind);
    }

    [Fact]
    public void HitTest_does_not_select_segment_when_pointer_is_close_to_line()
    {
        var definition = RouteMapSeed.Create();
        var transform = RouteMapTransform.Create(
            definition.LogicalWidth,
            definition.LogicalHeight,
            1120,
            700,
            padding: 0);

        var hit = RouteMapHitTester.HitTest(
            definition,
            RouteMapRuntimeState.Empty,
            transform.ToViewPoint(90, 525),
            transform);

        Assert.Null(hit);
    }

    [Fact]
    public void HitTest_returns_null_for_empty_area()
    {
        var definition = RouteMapSeed.Create();
        var transform = RouteMapTransform.Create(
            definition.LogicalWidth,
            definition.LogicalHeight,
            1120,
            700,
            padding: 0);

        var hit = RouteMapHitTester.HitTest(
            definition,
            RouteMapRuntimeState.Empty,
            transform.ToViewPoint(1120, 20),
            transform);

        Assert.Null(hit);
    }

    [Fact]
    public void HitTest_ignores_disabled_node()
    {
        var definition = RouteMapSeed.Create();
        var transform = RouteMapTransform.Create(
            definition.LogicalWidth,
            definition.LogicalHeight,
            1120,
            700,
            padding: 0);
        var bsu1 = definition.Nodes.Single(x => x.Id == "bsu_1");
        var runtime = RouteMapRuntimeState.Empty with
        {
            Objects = new Dictionary<string, RouteObjectRuntimeState>
            {
                [bsu1.Id] = new(
                    bsu1.Id,
                    RouteObjectState.Disabled,
                    Text: null,
                    ValueText: null,
                    IsVisible: true,
                    CanStart: false,
                    CanStop: false,
                    IsEnabled: false)
            }
        };

        var hit = RouteMapHitTester.HitTest(
            definition,
            runtime,
            transform.ToViewPoint(bsu1.X, bsu1.Y),
            transform);

        Assert.Null(hit);
    }
}
