using Avalonia;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class RouteMapViewportLayoutTests
{
    [Fact]
    public void DeadEnd_bounds_include_both_caps_and_the_widest_stroke()
    {
        var definition = CreateTerminalMap();
        var bounds = RouteMapBoundsCalculator.CalculateDefinitionBounds(definition);

        Assert.Equal(new Rect(-26, -28, 215, 137), bounds);
    }

    [Theory]
    [InlineData(800, 600)]
    [InlineData(1024, 768)]
    [InlineData(1142, 749)]
    [InlineData(1280, 720)]
    [InlineData(1366, 768)]
    [InlineData(1920, 1080)]
    public void DeadEnd_caps_fit_with_stroke_and_keep_their_proportions_after_resize(double width, double height)
    {
        var definition = CreateTerminalMap();
        var first = RouteMapViewportLayout.Create(definition, width, height);
        RouteMapViewportLayout.Create(definition, 500, 400);
        var layout = RouteMapViewportLayout.Create(definition, width, height);
        var transform = layout.Transform;
        Assert.Equal(first.Transform.Scale, transform.Scale);
        Assert.Equal(first.Transform.OffsetX, transform.OffsetX);
        Assert.Equal(first.Transform.OffsetY, transform.OffsetY);

        var nodes = definition.Nodes.ToDictionary(x => x.Id);
        foreach (var segment in definition.Segments)
        {
            var from = nodes[segment.FromNodeId];
            var to = nodes[segment.ToNodeId];
            var style = segment.Style!;
            Assert.True(RouteSegmentGeometry.TryCreateTerminalCap(
                segment, style, new Point(from.X, from.Y), new Point(to.X, to.Y), out var cap));
            var start = transform.ToViewPoint(cap.Start.X, cap.Start.Y);
            var end = transform.ToViewPoint(cap.End.X, cap.End.Y);
            var stroke = style.Thickness * transform.Scale;
            var paintedBounds = RouteSegmentGeometry.BoundsOf(start, end).Inflate(stroke / 2);

            Assert.Equal(80d / 12, RouteSegmentGeometry.Distance(start, end) / stroke, precision: 8);
            Assert.True(paintedBounds.Left >= -1e-8 && paintedBounds.Right <= width + 1e-8);
            Assert.True(paintedBounds.Top >= -1e-8 && paintedBounds.Bottom <= height + 1e-8);
        }
    }

    [Fact]
    public void Invalid_or_unrendered_terminal_geometry_does_not_affect_the_viewport()
    {
        var definition = CreateTerminalMap();
        var nodes = definition.Nodes.ToArray();
        nodes[1] = nodes[1] with { X = double.NaN };
        nodes[3] = nodes[3] with { Y = 10_000 };
        var segments = definition.Segments.ToArray();
        segments[1] = segments[1] with { IsVisible = false };

        var layout = RouteMapViewportLayout.Create(
            definition with { Nodes = nodes, Segments = segments }, viewWidth: 0, viewHeight: double.PositiveInfinity);

        var stationsOnly = definition with
        {
            Nodes = definition.Nodes.Where(node => node.Kind != RouteNodeKind.TerminalAnchor).ToArray(),
            Segments = [],
        };
        Assert.Equal(RouteMapBoundsCalculator.CalculateDefinitionBounds(stationsOnly), layout.LogicalRouteBounds);
        Assert.True(double.IsFinite(layout.Transform.Scale) && layout.Transform.Scale > 0);
    }

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

    private static RouteMapDefinition CreateTerminalMap()
    {
        var lowerStation = new RouteNode("lower-station", "", 20, 42, RouteNodeKind.ServicePoint, RouteObjectState.Idle, []);
        var lowerAnchor = lowerStation with { Id = "lower-anchor", Y = 100, Kind = RouteNodeKind.TerminalAnchor };
        var rightStation = lowerStation with { Id = "right-station", X = 120, Y = 18 };
        var rightAnchor = rightStation with { Id = "right-anchor", X = 180, Kind = RouteNodeKind.TerminalAnchor };
        var lower = new RouteSegment("lower", lowerStation.Id, lowerAnchor.Id, RouteObjectState.Idle, false, [],
            RouteSegmentKind.DeadEnd, Style: new RouteSegmentStyle { Thickness = 12, ActiveThickness = 18 })
        {
            TerminalSide = RouteTerminalSide.To,
            TerminalCapLength = 80,
        };
        var right = lower with { Id = "right", FromNodeId = rightAnchor.Id, ToNodeId = rightStation.Id, TerminalSide = RouteTerminalSide.From };
        return new RouteMapDefinition(180, 100, [], [lowerStation, lowerAnchor, rightStation, rightAnchor], [lower, right],
            [], [], [], [], new RouteMapDisplaySettings { MapPadding = 0 });
    }
}
