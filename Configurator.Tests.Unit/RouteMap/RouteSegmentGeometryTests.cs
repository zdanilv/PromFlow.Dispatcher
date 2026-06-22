using Avalonia;
using Avalonia.Media;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class RouteSegmentGeometryTests
{
    [Fact]
    public void Rounded_elbow_builds_tangent_lines_and_quarter_circle()
    {
        var path = RouteSegmentGeometry.CreateRoundedElbow(
            new Point(90, 460),
            new Point(560, 100),
            requestedRadius: 260,
            RouteElbowOrder.VerticalThenHorizontal);

        Assert.Equal(3, path.Parts.Count);
        var firstLine = Assert.IsType<RouteLinePathPart>(path.Parts[0]);
        var arc = Assert.IsType<RouteArcPathPart>(path.Parts[1]);
        var secondLine = Assert.IsType<RouteLinePathPart>(path.Parts[2]);

        Assert.Equal(new Point(90, 460), firstLine.Start);
        Assert.Equal(new Point(90, 360), firstLine.End);
        Assert.Equal(new Point(350, 360), arc.Center);
        Assert.Equal(260, arc.Radius);
        AssertPoint(new Point(90, 360), arc.Start);
        AssertPoint(new Point(350, 100), arc.End);
        Assert.Equal(new Point(350, 100), secondLine.Start);
        Assert.Equal(new Point(560, 100), secondLine.End);
        Assert.Equal(90, path.Bounds.X, precision: 8);
        Assert.Equal(100, path.Bounds.Y, precision: 8);
        Assert.Equal(470, path.Bounds.Width, precision: 8);
        Assert.Equal(360, path.Bounds.Height, precision: 8);
        Assert.Equal(100 + Math.PI * 260 / 2 + 210, path.Length, precision: 8);
    }

    [Fact]
    public void Rounded_elbow_clamps_radius_to_shortest_axis()
    {
        var path = RouteSegmentGeometry.CreateRoundedElbow(
            new Point(90, 460),
            new Point(560, 100),
            requestedRadius: 500,
            RouteElbowOrder.VerticalThenHorizontal);

        var arc = Assert.Single(path.Parts.OfType<RouteArcPathPart>());
        Assert.Equal(360, arc.Radius);
        AssertPoint(new Point(90, 460), arc.Start);
        AssertPoint(new Point(450, 100), arc.End);
    }

    [Theory]
    [InlineData(150, new double[] { 0, 150 })]
    [InlineData(250, new double[] { 0, 100, 106, 250 })]
    [InlineData(350, new double[] { 0, 100, 106, 206, 212, 350 })]
    public void CalculateVisibleRanges_keeps_every_tail_at_least_100_pixels(
        double totalLength,
        double[] expectedEdges)
    {
        var ranges = RouteSegmentGeometry.CalculateVisibleRanges(totalLength);
        var actualEdges = ranges.SelectMany(x => new[] { x.Start, x.End }).ToArray();

        Assert.Equal(expectedEdges, actualEdges);
        Assert.All(ranges, x => Assert.True(x.Length >= 100));
    }

    [Fact]
    public void Visible_ranges_continue_across_line_and_arc_by_accumulated_length()
    {
        var path = RouteSegmentGeometry.CreateRoundedElbow(
            new Point(0, 200),
            new Point(300, 0),
            requestedRadius: 100,
            RouteElbowOrder.VerticalThenHorizontal);
        var ranges = RouteSegmentGeometry.CalculateVisibleRanges(path.Length);

        AssertPoint(new Point(0, 100), path.PointAt(100));
        Assert.Equal(106, ranges[1].Start);
        Assert.NotEqual(path.PointAt(100), path.PointAt(ranges[1].Start));
        Assert.IsType<RouteArcPathPart>(path.Parts[1]);
    }

    [Fact]
    public void Drawable_ranges_trim_both_node_endpoints_before_fragmenting()
    {
        var ranges = RouteSegmentGeometry.CalculateDrawableRanges(
            totalLength: 250,
            startTrim: 21,
            endTrim: 21,
            fragmentLength: 100,
            gap: 6);

        Assert.Equal([new RoutePathRange(21, 121), new RoutePathRange(127, 229)], ranges);
    }

    [Fact]
    public void Drawable_range_uses_same_path_distance_for_elbow_endpoints()
    {
        var path = RouteSegmentGeometry.CreateRoundedElbow(
            new Point(0, 200),
            new Point(300, 0),
            requestedRadius: 100,
            RouteElbowOrder.VerticalThenHorizontal);
        var range = Assert.Single(RouteSegmentGeometry.CalculateDrawableRanges(
            path.Length,
            startTrim: 21,
            endTrim: path.Length - 80));

        AssertPoint(path.PointAt(21), path.PointAt(range.Start));
        AssertPoint(path.PointAt(80), path.PointAt(range.End));
    }

    [Fact]
    public void Logical_drawable_ranges_are_projected_without_refragmenting_for_viewport_scale()
    {
        var definition = RouteMapSeed.Create();
        var segment = definition.Segments.Single(x => x.Id == "bsu2_to_bucket");
        var nodes = definition.Nodes.ToDictionary(x => x.Id);
        var from = nodes[segment.FromNodeId];
        var to = nodes[segment.ToNodeId];
        var display = definition.Display ?? new RouteMapDisplaySettings();
        var logicalPath = RouteSegmentGeometry.Create(
            segment,
            new Point(from.X, from.Y),
            new Point(to.X, to.Y));
        var viewPath = RouteSegmentGeometry.Create(
            segment with { ArcRadius = segment.ArcRadius * 0.45 },
            new Point(from.X * 0.45, from.Y * 0.45),
            new Point(to.X * 0.45, to.Y * 0.45));

        var logicalRanges = RouteSegmentGeometry.CalculateLogicalDrawableRanges(segment, from, to, display);
        var projected = RouteSegmentGeometry.ProjectRanges(logicalRanges, logicalPath.Length, viewPath.Length);

        Assert.Equal(3, logicalRanges.Count);
        Assert.Equal(logicalRanges.Count, projected.Count);
        for (var index = 0; index < logicalRanges.Count; index++)
        {
            Assert.Equal(logicalRanges[index].Start / logicalPath.Length, projected[index].Start / viewPath.Length, precision: 8);
            Assert.Equal(logicalRanges[index].End / logicalPath.Length, projected[index].End / viewPath.Length, precision: 8);
        }
    }

    [Fact]
    public void View_drawable_ranges_use_screen_length_after_viewport_scale()
    {
        var definition = RouteMapSeed.Create();
        var segment = definition.Segments.Single(x => x.Id == "bucket_to_upper_dead_end");
        var nodes = definition.Nodes.ToDictionary(x => x.Id);
        var from = nodes[segment.FromNodeId];
        var to = nodes[segment.ToNodeId];
        var display = definition.Display ?? new RouteMapDisplaySettings();
        const double scale = 0.45;
        var viewPath = RouteSegmentGeometry.Create(
            segment with { ArcRadius = segment.ArcRadius * scale },
            new Point(from.X * scale, from.Y * scale),
            new Point(to.X * scale, to.Y * scale));

        var visualRange = Assert.Single(RouteSegmentGeometry.CalculateViewDrawableRanges(
            viewPath,
            segment,
            from,
            to,
            display));
        var logicalRanges = RouteSegmentGeometry.CalculateLogicalDrawableRanges(segment, from, to, display);

        Assert.Equal(2, logicalRanges.Count);
        Assert.Equal(21, visualRange.Start, precision: 8);
        Assert.Equal(viewPath.Length - 21, visualRange.End, precision: 8);
    }

    [Fact]
    public void View_drawable_ranges_use_scaled_elbow_path_length_including_arc()
    {
        var definition = RouteMapSeed.Create();
        var segment = definition.Segments.Single(x => x.Id == "bsu2_to_bucket");
        var nodes = definition.Nodes.ToDictionary(x => x.Id);
        var from = nodes[segment.FromNodeId];
        var to = nodes[segment.ToNodeId];
        var display = definition.Display ?? new RouteMapDisplaySettings();
        const double scale = 0.8;
        var viewPath = RouteSegmentGeometry.Create(
            segment with { ArcRadius = segment.ArcRadius * scale },
            new Point(from.X * scale, from.Y * scale),
            new Point(to.X * scale, to.Y * scale));

        var ranges = RouteSegmentGeometry.CalculateViewDrawableRanges(viewPath, segment, from, to, display);

        Assert.IsType<RouteArcPathPart>(viewPath.Parts[1]);
        Assert.Equal(3, ranges.Count);
        Assert.Equal(21, ranges[0].Start, precision: 8);
        Assert.Equal(100, ranges[0].Length, precision: 8);
        Assert.Equal(100, ranges[1].Length, precision: 8);
        Assert.True(ranges[2].Length >= 100);
    }

    [Fact]
    public void Track_pen_uses_configured_round_or_flat_cap()
    {
        var palette = new RouteMapPaletteSettings();
        var round = RouteMapPalette.TrackPenForState(RouteObjectState.Idle, palette, new RouteSegmentStyle());
        var flat = RouteMapPalette.TrackPenForState(
            RouteObjectState.Idle,
            palette,
            new RouteSegmentStyle { LineCap = RouteLineCap.Flat });

        Assert.Equal(PenLineCap.Round, round.LineCap);
        Assert.Equal(PenLineCap.Flat, flat.LineCap);
    }

    private static void AssertPoint(Point expected, Point actual)
    {
        Assert.Equal(expected.X, actual.X, precision: 8);
        Assert.Equal(expected.Y, actual.Y, precision: 8);
    }
}
