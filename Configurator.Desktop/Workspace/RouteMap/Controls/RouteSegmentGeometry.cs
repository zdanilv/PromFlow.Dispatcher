using Avalonia;
using Avalonia.Media;
using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.Controls;

internal abstract record RoutePathPart
{
    public abstract Point Start { get; }
    public abstract Point End { get; }
    public abstract double Length { get; }
    public abstract Rect Bounds { get; }
    public abstract Point PointAt(double distance);
}

internal sealed record RouteLinePathPart : RoutePathPart
{
    public RouteLinePathPart(Point start, Point end)
    {
        Start = start;
        End = end;
    }

    public override Point Start { get; }
    public override Point End { get; }
    public override double Length => RouteSegmentGeometry.Distance(Start, End);

    public override Rect Bounds => RouteSegmentGeometry.BoundsOf(Start, End);

    public override Point PointAt(double distance)
    {
        if (Length <= 0)
            return Start;

        var ratio = Math.Clamp(distance / Length, 0, 1);
        return new Point(
            Start.X + (End.X - Start.X) * ratio,
            Start.Y + (End.Y - Start.Y) * ratio);
    }
}

internal sealed record RouteArcPathPart(
    Point Center,
    double Radius,
    double StartAngle,
    double SweepAngle) : RoutePathPart
{
    public override Point Start => PointAt(0);
    public override Point End => PointAt(Length);
    public override double Length => Math.Abs(SweepAngle) * Radius;

    public override Rect Bounds
    {
        get
        {
            var start = Start;
            var end = End;
            var minX = Math.Min(Center.X, Math.Min(start.X, end.X));
            var minY = Math.Min(Center.Y, Math.Min(start.Y, end.Y));
            var maxX = Math.Max(Center.X, Math.Max(start.X, end.X));
            var maxY = Math.Max(Center.Y, Math.Max(start.Y, end.Y));
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }
    }

    public override Point PointAt(double distance)
    {
        if (Radius <= 0 || Length <= 0)
            return Center;

        var ratio = Math.Clamp(distance / Length, 0, 1);
        var angle = StartAngle + SweepAngle * ratio;
        return new Point(
            Center.X + Math.Cos(angle) * Radius,
            Center.Y + Math.Sin(angle) * Radius);
    }
}

internal sealed class RouteSegmentPath
{
    public RouteSegmentPath(IReadOnlyList<RoutePathPart> parts)
    {
        Parts = parts.Where(x => x.Length > 0).ToArray();
        Length = Parts.Sum(x => x.Length);
        Bounds = RouteSegmentGeometry.UnionBounds(Parts.Select(x => x.Bounds));
    }

    public IReadOnlyList<RoutePathPart> Parts { get; }
    public double Length { get; }
    public Rect Bounds { get; }
    public Point Start => Parts.Count > 0 ? Parts[0].Start : default;
    public Point End => Parts.Count > 0 ? Parts[^1].End : default;

    public Point PointAt(double distance)
    {
        if (Parts.Count == 0)
            return default;

        var remaining = Math.Clamp(distance, 0, Length);
        foreach (var part in Parts)
        {
            if (remaining <= part.Length)
                return part.PointAt(remaining);

            remaining -= part.Length;
        }

        return End;
    }
}

internal readonly record struct RoutePathRange(double Start, double End)
{
    public double Length => Math.Max(0, End - Start);
}

internal static class RouteSegmentGeometry
{
    public const double VisibleFragmentLength = 100;
    public const double FragmentGap = 6;

    public static RouteSegmentPath Create(RouteSegment segment, Point start, Point end)
    {
        if (segment.Kind == RouteSegmentKind.Straight)
            return new RouteSegmentPath([new RouteLinePathPart(start, end)]);

        return CreateRoundedElbow(start, end, segment.ArcRadius, segment.ElbowOrder);
    }

    public static RouteSegmentPath CreateRoundedElbow(
        Point start,
        Point end,
        double requestedRadius,
        RouteElbowOrder order)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var radius = Math.Clamp(requestedRadius, 0, Math.Min(Math.Abs(dx), Math.Abs(dy)));

        if (Math.Abs(dx) <= double.Epsilon || Math.Abs(dy) <= double.Epsilon)
            return new RouteSegmentPath([new RouteLinePathPart(start, end)]);

        var sx = Math.Sign(dx);
        var sy = Math.Sign(dy);
        var parts = new List<RoutePathPart>();

        if (order == RouteElbowOrder.VerticalThenHorizontal)
        {
            var firstTangent = new Point(start.X, end.Y - sy * radius);
            var center = new Point(start.X + sx * radius, end.Y - sy * radius);
            var secondTangent = new Point(start.X + sx * radius, end.Y);

            AddLine(parts, start, firstTangent);
            AddArc(parts, center, radius, firstTangent, -sx * sy * Math.PI / 2);
            AddLine(parts, secondTangent, end);
        }
        else
        {
            var firstTangent = new Point(end.X - sx * radius, start.Y);
            var center = new Point(end.X - sx * radius, start.Y + sy * radius);
            var secondTangent = new Point(end.X, start.Y + sy * radius);

            AddLine(parts, start, firstTangent);
            AddArc(parts, center, radius, firstTangent, sx * sy * Math.PI / 2);
            AddLine(parts, secondTangent, end);
        }

        return new RouteSegmentPath(parts);
    }

    public static IReadOnlyList<RoutePathRange> CalculateVisibleRanges(
        double totalLength,
        double fragmentLength = VisibleFragmentLength,
        double gap = FragmentGap)
    {
        if (totalLength <= 0)
            return Array.Empty<RoutePathRange>();

        var normalizedFragment = Math.Max(1, fragmentLength);
        var normalizedGap = Math.Max(0, gap);
        if (totalLength < normalizedFragment * 2 + normalizedGap)
            return [new RoutePathRange(0, totalLength)];

        var ranges = new List<RoutePathRange>();
        var start = 0d;

        while (totalLength - start >= normalizedFragment * 2 + normalizedGap)
        {
            var end = start + normalizedFragment;
            ranges.Add(new RoutePathRange(start, end));
            start = end + normalizedGap;
        }

        ranges.Add(new RoutePathRange(start, totalLength));
        return ranges;
    }

    public static IReadOnlyList<RoutePathRange> CalculateDrawableRanges(
        double totalLength,
        double startTrim,
        double endTrim,
        double fragmentLength = VisibleFragmentLength,
        double gap = FragmentGap)
    {
        var normalizedStart = Math.Max(0, startTrim);
        var normalizedEnd = Math.Max(0, endTrim);
        var drawableLength = totalLength - normalizedStart - normalizedEnd;
        if (drawableLength <= 0)
            return Array.Empty<RoutePathRange>();

        return CalculateVisibleRanges(drawableLength, fragmentLength, gap)
            .Select(range => new RoutePathRange(
                range.Start + normalizedStart,
                range.End + normalizedStart))
            .ToArray();
    }

    public static IReadOnlyList<RoutePathRange> CalculateLogicalDrawableRanges(
        RouteSegment segment,
        RouteNode from,
        RouteNode to,
        RouteMapDisplaySettings display)
    {
        var path = Create(
            segment,
            new Point(from.X, from.Y),
            new Point(to.X, to.Y));
        var style = segment.Style ?? new RouteSegmentStyle();
        return CalculateDrawableRanges(
            path.Length,
            RadiusForNode(from) + style.EndpointGap,
            RadiusForNode(to) + style.EndpointGap,
            style.FragmentLength ?? display.FragmentLength,
            style.FragmentGap ?? display.FragmentGap);
    }

    public static IReadOnlyList<RoutePathRange> CalculateViewDrawableRanges(
        RouteSegmentPath path,
        RouteSegment segment,
        RouteNode from,
        RouteNode to,
        RouteMapDisplaySettings display)
    {
        var style = segment.Style ?? new RouteSegmentStyle();
        return CalculateDrawableRanges(
            path.Length,
            RadiusForNode(from) + style.EndpointGap,
            RadiusForNode(to) + style.EndpointGap,
            style.FragmentLength ?? display.FragmentLength,
            style.FragmentGap ?? display.FragmentGap);
    }

    public static IReadOnlyList<RoutePathRange> ProjectRanges(
        IReadOnlyList<RoutePathRange> ranges,
        double sourceLength,
        double targetLength)
    {
        if (ranges.Count == 0 || sourceLength <= 0 || targetLength <= 0)
            return Array.Empty<RoutePathRange>();

        var ratio = targetLength / sourceLength;
        return ranges
            .Select(range => new RoutePathRange(range.Start * ratio, range.End * ratio))
            .ToArray();
    }

    public static StreamGeometry CreateGeometry(RouteSegmentPath path, RoutePathRange range)
    {
        var geometry = new StreamGeometry();
        if (path.Parts.Count == 0 || range.Length <= 0)
            return geometry;

        using var context = geometry.Open();
        var rangeStart = Math.Clamp(range.Start, 0, path.Length);
        var rangeEnd = Math.Clamp(range.End, rangeStart, path.Length);
        context.BeginFigure(path.PointAt(rangeStart), isFilled: false);

        var partStart = 0d;
        foreach (var part in path.Parts)
        {
            var partEnd = partStart + part.Length;
            var sliceStart = Math.Max(rangeStart, partStart);
            var sliceEnd = Math.Min(rangeEnd, partEnd);

            if (sliceEnd > sliceStart)
                AppendPart(context, part, sliceStart - partStart, sliceEnd - partStart);

            if (partEnd >= rangeEnd)
                break;

            partStart = partEnd;
        }

        context.EndFigure(isClosed: false);
        return geometry;
    }

    internal static Rect UnionBounds(IEnumerable<Rect> bounds)
    {
        var hasBounds = false;
        var result = default(Rect);
        foreach (var item in bounds)
        {
            result = hasBounds ? result.Union(item) : item;
            hasBounds = true;
        }

        return result;
    }

    internal static Rect BoundsOf(Point a, Point b)
    {
        return new Rect(
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Abs(a.X - b.X),
            Math.Abs(a.Y - b.Y));
    }

    internal static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double RadiusForNode(RouteNode node) => (node.Style ?? new RouteNodeStyle()).Radius;

    private static void AddLine(ICollection<RoutePathPart> parts, Point start, Point end)
    {
        if (Distance(start, end) > double.Epsilon)
            parts.Add(new RouteLinePathPart(start, end));
    }

    private static void AddArc(
        ICollection<RoutePathPart> parts,
        Point center,
        double radius,
        Point start,
        double sweepAngle)
    {
        if (radius <= double.Epsilon)
            return;

        var startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X);
        parts.Add(new RouteArcPathPart(center, radius, startAngle, sweepAngle));
    }

    private static void AppendPart(
        StreamGeometryContext context,
        RoutePathPart part,
        double localStart,
        double localEnd)
    {
        if (part is RouteLinePathPart line)
        {
            context.LineTo(line.PointAt(localEnd));
            return;
        }

        if (part is RouteArcPathPart arc)
        {
            var sweep = arc.SweepAngle >= 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise;
            context.PreciseArcTo(
                arc.PointAt(localEnd),
                new Size(arc.Radius, arc.Radius),
                rotationAngle: 0,
                isLargeArc: false,
                sweep);
        }
    }
}
