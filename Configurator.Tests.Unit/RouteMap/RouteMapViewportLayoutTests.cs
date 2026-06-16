using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class RouteMapViewportLayoutTests
{
    [Fact]
    public void Create_centers_route_inside_area_left_of_card_column()
    {
        var definition = RouteMapSeed.Create();

        var layout = RouteMapViewportLayout.Create(definition, viewWidth: 1500, viewHeight: 800);
        var routeCenter = layout.Transform.ToViewPoint(
            layout.LogicalRouteBounds.Center.X,
            layout.LogicalRouteBounds.Center.Y);

        Assert.Equal(RouteMapViewportLayout.DefaultCardWidth + RouteMapViewportLayout.CardGap, layout.ReservedRightWidth);
        Assert.Equal(layout.MapViewBounds.Center.X, routeCenter.X, precision: 8);
        Assert.Equal(layout.MapViewBounds.Center.Y, routeCenter.Y, precision: 8);
        Assert.Equal(1500 - layout.ReservedRightWidth, layout.MapViewBounds.Width);
    }

    [Fact]
    public void Create_keeps_top_anchor_clear_for_default_card_height()
    {
        var definition = RouteMapSeed.Create();
        var layout = RouteMapViewportLayout.Create(definition, viewWidth: 1500, viewHeight: 800);
        var concreteBucket = definition.Nodes.Single(x => x.Id == "concrete_bucket");
        var anchorY = layout.Transform.ToViewPoint(concreteBucket.X, concreteBucket.Y).Y;

        Assert.True(anchorY >= RouteMapViewportLayout.DefaultCardHeight / 2);
    }

    [Fact]
    public void Shorter_route_bounds_do_not_increase_scale_when_logical_canvas_is_unchanged()
    {
        var compact = RouteMapSeed.Create();
        var expanded = compact with
        {
            Nodes = compact.Nodes.Select(node => node.Id switch
            {
                "dead_end_lower" => node with { X = 90, Y = 720 },
                "bsu_1" => node with { X = 90, Y = 590 },
                "bsu_2" => node with { X = 90, Y = 460 },
                "concrete_bucket" => node with { X = 560, Y = 100 },
                "dead_end_upper" => node with { X = 1120, Y = 100 },
                _ => node,
            }).ToArray(),
        };

        var compactLayout = RouteMapViewportLayout.Create(compact, 1500, 800);
        var expandedLayout = RouteMapViewportLayout.Create(expanded, 1500, 800);

        Assert.Equal(expandedLayout.Transform.Scale, compactLayout.Transform.Scale, precision: 8);
    }

    [Fact]
    public void Right_node_label_starts_after_radius_and_is_vertically_centered()
    {
        var definition = RouteMapSeed.Create();
        var layout = RouteMapViewportLayout.Create(definition, 1500, 800);
        var node = definition.Nodes.Single(x => x.Id == "bsu_1");
        var center = layout.Transform.ToViewPoint(node.X, node.Y);

        var label = RouteNodeLabelLayout.Calculate(node, layout.Transform, textHeight: 12);

        Assert.False(label.IsHorizontallyCentered);
        Assert.Equal(center.X + RouteMapNodeMetrics.RadiusForNode(node) + 10, label.X, precision: 8);
        Assert.Equal(center.Y - 6, label.Top, precision: 8);
    }

    [Fact]
    public void Below_node_label_is_centered_and_starts_below_radius()
    {
        var definition = RouteMapSeed.Create();
        var layout = RouteMapViewportLayout.Create(definition, 1500, 800);
        var node = definition.Nodes.Single(x => x.Id == "concrete_bucket");
        var center = layout.Transform.ToViewPoint(node.X, node.Y);

        var label = RouteNodeLabelLayout.Calculate(node, layout.Transform, textHeight: 12);

        Assert.True(label.IsHorizontallyCentered);
        Assert.Equal(center.X, label.X, precision: 8);
        Assert.Equal(center.Y + RouteMapNodeMetrics.RadiusForNode(node) + 6, label.Top, precision: 8);
    }
}
