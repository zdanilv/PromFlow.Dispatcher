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

internal readonly record struct RouteTerminalCap(Point Start, Point End);

internal static class RouteSegmentGeometry
{
    public const double VisibleFragmentLength = 100;
    public const double FragmentGap = 6;

    public static RouteSegmentPath Create(RouteSegment segment, Point start, Point end, double radiusScale = 1)
    {
        if (segment.Kind == RouteSegmentKind.DeadEnd && !HasUsableLength(Distance(start, end)))
            return new RouteSegmentPath([]);

        if (segment.Kind is RouteSegmentKind.Straight or RouteSegmentKind.DeadEnd)
            return new RouteSegmentPath([new RouteLinePathPart(start, end)]);

        return CreateRoundedElbow(start, end, segment.ArcRadius * radiusScale, segment.ElbowOrder);
    }

    public static bool TryCreateTerminalCap(
        RouteSegment segment,
        RouteSegmentStyle style,
        Point start,
        Point end,
        out RouteTerminalCap cap)
    {
        cap = default;
        if (segment.Kind != RouteSegmentKind.DeadEnd ||
            segment.TerminalSide is not (RouteTerminalSide.From or RouteTerminalSide.To) ||
            !double.IsFinite(style.Thickness) || style.Thickness <= 0)
            return false;

        var length = Distance(start, end);
        var halfCapLength = (segment.TerminalCapLength ?? 7 * style.Thickness) / 2;
        if (!HasUsableLength(length) || !double.IsFinite(halfCapLength) || halfCapLength <= 0)
            return false;

        var center = segment.TerminalSide == RouteTerminalSide.From ? start : end;
        var arm = new Vector(-(end.Y - start.Y) / length, (end.X - start.X) / length) * halfCapLength;
        var capStart = center - arm;
        var capEnd = center + arm;
        if (!HasUsableLength(Distance(capStart, capEnd)))
            return false;

        cap = new RouteTerminalCap(capStart, capEnd);
        return true;
    }

    public static bool TryCreateTerminalViewGeometry(
        RouteSegment segment,
        RouteNode from,
        RouteNode to,
        RouteSegmentStyle style,
        RouteMapTransform transform,
        Rect viewBounds,
        out Point start,
        out Point end,
        out RouteTerminalCap cap)
    {
        start = transform.ToViewPoint(from.X, from.Y);
        end = transform.ToViewPoint(to.X, to.Y);
        cap = default;
        if (!TryCreateTerminalCap(segment, style, new Point(from.X, from.Y), new Point(to.X, to.Y), out var logicalCap))
            return false;

        var terminalAtStart = segment.TerminalSide == RouteTerminalSide.From;
        var station = terminalAtStart ? to : from;
        var stationStyle = station.Style ?? new RouteNodeStyle();
        Vector outward = terminalAtStart ? start - end : end - start;
        var length = outward.Length;
        if (!HasUsableLength(length))
            return false;

        // Station circles use a fixed DIP radius. Leave room for the short body when the map shrinks.
        var halfStroke = Math.Max(style.Thickness, TerminalActiveThickness(style)) * transform.Scale / 2;
        var minimumLength = stationStyle.Radius + stationStyle.BorderThickness / 2 + halfStroke + 6;
        cap = new RouteTerminalCap(
            transform.ToViewPoint(logicalCap.Start.X, logicalCap.Start.Y),
            transform.ToViewPoint(logicalCap.End.X, logicalCap.End.Y));
        var direction = outward / length;
        var capBounds = BoundsOf(cap.Start, cap.End).Inflate(style.Thickness * transform.Scale / 2);
        var horizontalRoom = direction.X >= 0 ? viewBounds.Right - capBounds.Right : capBounds.Left - viewBounds.Left;
        var verticalRoom = direction.Y >= 0 ? viewBounds.Bottom - capBounds.Bottom : capBounds.Top - viewBounds.Top;
        var extension = Math.Max(0, Math.Min(minimumLength - length, Math.Min(
            direction.X == 0 ? double.PositiveInfinity : horizontalRoom / Math.Abs(direction.X),
            direction.Y == 0 ? double.PositiveInfinity : verticalRoom / Math.Abs(direction.Y))));
        var shift = direction * extension;
        if (terminalAtStart)
            start += shift;
        else
            end += shift;
        cap = new RouteTerminalCap(cap.Start + shift, cap.End + shift);
        return true;
    }

    public static double TerminalActiveThickness(RouteSegmentStyle style) =>
        double.IsFinite(style.ActiveThickness) && style.ActiveThickness > 0
            ? style.ActiveThickness
            : style.Thickness;

    public static StreamGeometry CreateTerminalBodyClip(RouteSegment segment, Point start, Point end, double thickness)
    {
        var terminalAtStart = segment.TerminalSide == RouteTerminalSide.From;
        var terminal = terminalAtStart ? start : end;
        var station = terminalAtStart ? end : start;
        var padding = (station - terminal) * (thickness / Distance(station, terminal));
        var side = new Vector(-padding.Y, padding.X);
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(terminal - side, isFilled: true);
        context.LineTo(terminal + side);
        context.LineTo(station + padding + side);
        context.LineTo(station + padding - side);
        context.EndFigure(isClosed: true);
        return geometry;
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
        if (!double.IsFinite(totalLength) || totalLength <= 0 ||
            !double.IsFinite(fragmentLength) || !double.IsFinite(gap))
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
            EndpointTrim(from, style),
            EndpointTrim(to, style),
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
            EndpointTrim(from, style),
            EndpointTrim(to, style),
            style.FragmentLength ?? display.FragmentLength,
            style.FragmentGap ?? display.FragmentGap);
    }

    public static IReadOnlyList<RoutePathRange> CalculateTerminalViewDrawableRanges(
        RouteSegment segment,
        RouteNode station,
        RouteSegmentStyle style,
        RouteMapDisplaySettings display,
        double viewLength,
        double scale,
        IReadOnlyList<RoutePathRange> logicalRanges)
    {
        var nodeStyle = station.Style ?? new RouteNodeStyle();
        var configuredTrim = nodeStyle.Radius + style.EndpointGap;
        var halfStroke = Math.Max(style.Thickness, TerminalActiveThickness(style)) * scale / 2;
        var minimumTrim = nodeStyle.Radius + nodeStyle.BorderThickness / 2;
        if (style.LineCap == RouteLineCap.Round)
            minimumTrim += halfStroke;

        if (!HasUsableLength(viewLength) || !double.IsFinite(configuredTrim) ||
            !double.IsFinite(minimumTrim) || viewLength <= minimumTrim)
            return [];

        // Use the configured gap where it fits. Short approaches may use the spare
        // gap, while the stroke still stays outside the station's circle.
        var stationTrim = Math.Clamp(viewLength - halfStroke, minimumTrim, Math.Max(minimumTrim, configuredTrim));
        var terminalAtStart = segment.TerminalSide == RouteTerminalSide.From;
        var start = terminalAtStart ? 0 : stationTrim;
        var end = terminalAtStart ? viewLength - stationTrim : viewLength;

        if (logicalRanges.Count == 0)
            return CalculateDrawableRanges(viewLength, start, viewLength - end,
                style.FragmentLength ?? display.FragmentLength, style.FragmentGap ?? display.FragmentGap);

        // Project the configured fragments onto the drawable interval so their
        // indexes stay stable while the station's radius and gap stay in DIPs.
        var logicalStart = logicalRanges[0].Start;
        var ratio = (end - start) / (logicalRanges[^1].End - logicalStart);
        var projected = new RoutePathRange[logicalRanges.Count];
        for (var i = 0; i < projected.Length; i++)
        {
            projected[i] = new RoutePathRange(
                i == 0 ? start : start + (logicalRanges[i].Start - logicalStart) * ratio,
                i == projected.Length - 1 ? end : start + (logicalRanges[i].End - logicalStart) * ratio);
        }

        return projected;
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
        return double.Hypot(dx, dy);
    }

    private static bool HasUsableLength(double length) => double.IsFinite(length) && length > 1e-8;

    private static double EndpointTrim(RouteNode node, RouteSegmentStyle style) =>
        node.Kind == RouteNodeKind.TerminalAnchor ? 0 : RouteMapNodeMetrics.RadiusForNode(node) + style.EndpointGap;

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
