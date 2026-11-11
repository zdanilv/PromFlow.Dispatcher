using Avalonia;
using Avalonia.Media;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class RouteSegmentGeometryTests
{
    [Theory]
    [InlineData("lower_dead_end_to_bsu1", true)]
    [InlineData("wash_to_upper_dead_end", false)]
    public void Seed_dead_end_body_reaches_perpendicular_cap_in_compact_view(string id, bool horizontalCap)
    {
        var definition = RouteMapSeed.Create();
        var layout = RouteMapViewportLayout.Create(definition, 800, 600);
        var line = definition.Segments.Single(segment => segment.Id == id);
        var from = definition.Nodes.Single(node => node.Id == line.FromNodeId);
        var to = definition.Nodes.Single(node => node.Id == line.ToNodeId);
        var style = line.Style ?? new RouteSegmentStyle();
        Assert.True(RouteSegmentGeometry.TryCreateTerminalViewGeometry(
            line, from, to, style, layout.Transform, layout.MapViewBounds, out var start, out var end, out var cap));
        var station = horizontalCap ? to : from;
        var length = RouteSegmentGeometry.Distance(start, end);
        var body = Assert.Single(RouteSegmentGeometry.CalculateTerminalViewDrawableRanges(
            line, station, style, new RouteMapDisplaySettings(), length, layout.Transform.Scale, []));

        Assert.True(body.Length > 0);
        Assert.Equal(horizontalCap ? 0 : length, horizontalCap ? body.Start : body.End, precision: 8);
        Assert.Equal(horizontalCap ? cap.Start.Y : cap.Start.X, horizontalCap ? cap.End.Y : cap.End.X, precision: 8);
        var center = new Point((cap.Start.X + cap.End.X) / 2, (cap.Start.Y + cap.End.Y) / 2);
        AssertPoint(horizontalCap ? start : end, center);
    }

    [Theory]
    [InlineData(0, 100, false, -14, 0)]
    [InlineData(100, 0, false, 0, 14)]
    [InlineData(0, 100, true, -14, 0)]
    [InlineData(100, 0, true, 0, 14)]
    [InlineData(60, 80, false, -11.2, 8.4)]
    [InlineData(60, 80, true, -11.2, 8.4)]
    public void DeadEnd_builds_one_straight_body_and_a_symmetric_perpendicular_cap(
        double x, double y, bool reversed, double armX, double armY)
    {
        var station = new Point(10, 20);
        var terminal = new Point(station.X + x, station.Y + y);
        var (start, end) = reversed ? (terminal, station) : (station, terminal);
        var segment = DeadEnd(reversed);
        var path = RouteSegmentGeometry.Create(segment, start, end);

        Assert.IsType<RouteLinePathPart>(Assert.Single(path.Parts));
        Assert.Equal(start, path.Start);
        Assert.Equal(end, path.End);
        Assert.True(RouteSegmentGeometry.TryCreateTerminalCap(segment, new RouteSegmentStyle(), start, end, out var cap));
        var arm = new Vector(armX, armY) * (reversed ? -1 : 1);
        AssertPoint(terminal - arm, cap.Start);
        AssertPoint(terminal + arm, cap.End);
        Assert.Equal(0, x * (cap.End.X - cap.Start.X) + y * (cap.End.Y - cap.Start.Y), precision: 8);
    }

    [Theory]
    [InlineData(20, 1, false)]
    [InlineData(30, 0.7, true)]
    [InlineData(45, 0.55, false)]
    [InlineData(60, 0.8, true)]
    public void Short_DeadEnd_reaches_the_cap_and_clears_the_station(double length, double scale, bool reversed)
    {
        var segment = DeadEnd(reversed);
        var station = new RouteNode("station", "", 0, 0, RouteNodeKind.ServicePoint, RouteObjectState.Idle, []);
        var style = new RouteSegmentStyle();
        var viewLength = length * scale;
        var (start, end) = reversed ? (new Point(viewLength, 0), default(Point)) : (default, new Point(viewLength, 0));
        var path = RouteSegmentGeometry.Create(segment, start, end);
        var range = Assert.Single(RouteSegmentGeometry.CalculateTerminalViewDrawableRanges(
            segment, station, style, new RouteMapDisplaySettings(), path.Length, scale, []));

        var stationTrim = reversed ? path.Length - range.End : range.Start;
        Assert.True(range.Length > 0);
        Assert.True(stationTrim >= 15 + 1 + style.ActiveThickness * scale / 2);
        Assert.True(stationTrim <= 15 + style.EndpointGap);
        if (viewLength >= 15 + style.EndpointGap + style.ActiveThickness * scale / 2)
            Assert.Equal(15 + style.EndpointGap, stationTrim, precision: 8);
        AssertPoint(new Point(viewLength, 0), path.PointAt(reversed ? range.Start : range.End));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeadEnd_fragment_projection_preserves_indexes_and_has_no_trim_at_the_cap(bool reversed)
    {
        var segment = DeadEnd(reversed);
        var station = new RouteNode("station", "", 0, 0, RouteNodeKind.ServicePoint, RouteObjectState.Idle, []);
        var anchor = station with { Id = "anchor", X = 350, Kind = RouteNodeKind.TerminalAnchor };
        var (from, to) = reversed ? (anchor, station) : (station, anchor);
        var display = new RouteMapDisplaySettings();
        var logical = RouteSegmentGeometry.CalculateLogicalDrawableRanges(segment, from, to, display);
        var projected = RouteSegmentGeometry.CalculateTerminalViewDrawableRanges(
            segment, station, new RouteSegmentStyle(), display, viewLength: 175, scale: 0.5, logical);

        Assert.Equal(3, logical.Count);
        Assert.Equal(3, projected.Count);
        Assert.Equal(reversed ? 0 : 21, projected[0].Start, precision: 8);
        Assert.Equal(reversed ? 154 : 175, projected[^1].End, precision: 8);
        Assert.True(projected[1].Start > projected[0].End);
        Assert.True(projected[2].Start > projected[1].End);
    }

    [Theory]
    [InlineData(0, 0, 28, 4)]
    [InlineData(1e-12, 0, 28, 4)]
    [InlineData(double.NaN, 5, 28, 4)]
    [InlineData(5, double.PositiveInfinity, 28, 4)]
    [InlineData(20, 0, double.PositiveInfinity, 4)]
    [InlineData(20, 0, double.NaN, 4)]
    [InlineData(20, 0, 0, 4)]
    [InlineData(20, 0, -28, 4)]
    [InlineData(20, 0, 28, -4)]
    [InlineData(20, 0, 28, double.NaN)]
    public void DeadEnd_rejects_degenerate_coordinates_and_invalid_dimensions(double x, double y, double length, double thickness)
    {
        var segment = DeadEnd() with { TerminalCapLength = length };
        Assert.False(RouteSegmentGeometry.TryCreateTerminalCap(
            segment, new RouteSegmentStyle { Thickness = thickness }, default, new Point(x, y), out _));
    }

    [Theory]
    [InlineData(double.MaxValue, 5)]
    [InlineData(4, double.MaxValue)]
    public void DeadEnd_rejects_a_stroke_that_overflows_after_scaling(double thickness, double activeThickness)
    {
        var segment = DeadEnd() with { TerminalCapLength = 28 };
        var seed = RouteMapSeed.Create();
        var from = seed.Nodes.Single(node => node.Id == "wash");
        var to = seed.Nodes.Single(node => node.Id == "dead_end_upper");
        var transform = RouteMapTransform.Create(100, 100, 1000, 1000);

        Assert.False(RouteSegmentGeometry.TryCreateTerminalViewGeometry(segment, from, to,
            new RouteSegmentStyle { Thickness = thickness, ActiveThickness = activeThickness },
            transform, new Rect(0, 0, 1000, 1000), out _, out _, out _));
    }

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

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    public void Round_strokes_leave_the_projected_fragment_gaps_visible(double scale)
    {
        var seed = RouteMapSeed.Create();
        var segment = seed.Segments.Single(line => line.Id == "bucket_to_upper_dead_end");
        var from = seed.Nodes.Single(node => node.Id == segment.FromNodeId);
        var to = seed.Nodes.Single(node => node.Id == segment.ToNodeId);
        var logicalLength = to.X - from.X;
        var logical = RouteSegmentGeometry.CalculateLogicalDrawableRanges(segment, from, to, seed.Display!);
        var projected = RouteSegmentGeometry.ProjectRanges(logical, logicalLength, logicalLength * scale);
        var style = segment.Style!;

        var fitted = RouteSegmentGeometry.FitViewFragmentRanges(projected, logicalLength * scale,
            from, to, style, nodeScale: Math.Min(1, scale / 0.5), scale);

        Assert.Equal(2, fitted.Count);
        Assert.All(fitted, range => Assert.True(range.Length > 0));
        Assert.True(projected[1].Start - projected[0].End < style.Thickness);
        Assert.True(fitted[1].Start - fitted[0].End > style.ActiveThickness);
        Assert.Equal((projected[0].End + projected[1].Start) / 2,
            (fitted[0].End + fitted[1].Start) / 2, precision: 8);
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

    private static RouteSegment DeadEnd(bool reversed = false) =>
        new("dead-end", reversed ? "anchor" : "station", reversed ? "station" : "anchor",
            RouteObjectState.Idle, false, [], RouteSegmentKind.DeadEnd, ArcRadius: 50)
        {
            TerminalSide = reversed ? RouteTerminalSide.From : RouteTerminalSide.To,
        };

    private static void AssertPoint(Point expected, Point actual)
    {
        Assert.Equal(expected.X, actual.X, precision: 8);
        Assert.Equal(expected.Y, actual.Y, precision: 8);
    }
}
