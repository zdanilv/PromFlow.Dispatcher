using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class RouteMapHitTesterTests
{
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
