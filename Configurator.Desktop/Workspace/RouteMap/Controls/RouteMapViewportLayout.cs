using Avalonia;
using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.Controls;

internal sealed record RouteMapViewportLayout(
    RouteMapTransform Transform,
    Rect LogicalRouteBounds,
    Rect MapViewBounds,
    double ReservedRightWidth)
{
    public const double DefaultCardWidth = 295;
    public const double DefaultCardHeight = 141;
    public const double CardGap = 18;
    public const double MapPadding = 18;

    public static RouteMapViewportLayout Create(
        RouteMapDefinition definition,
        double viewWidth,
        double viewHeight)
    {
        var display = definition.Display ?? new RouteMapDisplaySettings();
        var logicalBounds = RouteMapBoundsCalculator.CalculateDefinitionBounds(definition);
        var attachedChains = definition.Chains
            .Where(x => !string.IsNullOrWhiteSpace(x.AttachedEquipmentCardId))
            .ToArray();
        var rightOffset = attachedChains.Length == 0
            ? 0
            : attachedChains.Max(x => Math.Max(0, x.AttachedCardRightOffset));
        var cardWidth = definition.MapEquipment
            .Where(x => x.IsVisible)
            .Select(x => (x.Style ?? new EquipmentCardStyle()).Width)
            .DefaultIfEmpty(DefaultCardWidth)
            .Max();
        var cardHeight = definition.MapEquipment
            .Where(x => x.IsVisible)
            .Select(x => (x.Style ?? new EquipmentCardStyle()).Height)
            .DefaultIfEmpty(DefaultCardHeight)
            .Max();
        var reservedRight = attachedChains.Length == 0
            ? 0
            : cardWidth + display.CardColumnGap + rightOffset;
        var mapWidth = double.IsFinite(viewWidth) ? Math.Max(1, viewWidth - reservedRight) : 1;
        var mapHeight = double.IsFinite(viewHeight) ? Math.Max(1, viewHeight) : 1;
        var mapViewBounds = new Rect(0, 0, mapWidth, mapHeight);
        var transform = RouteMapTransform.CreateForLogicalCanvas(
            logicalBounds,
            definition.LogicalWidth,
            definition.LogicalHeight,
            mapViewBounds,
            horizontalPadding: display.MapPadding,
            verticalPadding: attachedChains.Length == 0
                ? display.MapPadding
                : Math.Max(display.MapPadding, cardHeight / 2));

        return new RouteMapViewportLayout(transform, logicalBounds, mapViewBounds, reservedRight);
    }
}

internal static class RouteMapBoundsCalculator
{
    public static Rect CalculateDefinitionBounds(RouteMapDefinition definition)
    {
        var nodesById = definition.Nodes.ToDictionary(x => x.Id);
        var bounds = new List<Rect>();

        foreach (var node in definition.Nodes.Where(ContributesNodeBounds))
            bounds.Add(new Rect(node.X, node.Y, 0, 0));

        foreach (var segment in definition.Segments.Where(x => x.IsVisible))
        {
            if (!nodesById.TryGetValue(segment.FromNodeId, out var from) ||
                !nodesById.TryGetValue(segment.ToNodeId, out var to))
                continue;

            AddSegmentBounds(bounds, segment, from, to);
        }

        foreach (var vehicle in definition.Vehicles)
            bounds.Add(new Rect(vehicle.X, vehicle.Y, 0, 0));

        if (bounds.Count == 0)
            return new Rect(0, 0, Math.Max(1, definition.LogicalWidth), Math.Max(1, definition.LogicalHeight));

        var result = RouteSegmentGeometry.UnionBounds(bounds);
        return new Rect(
            result.X,
            result.Y,
            Math.Max(1, result.Width),
            Math.Max(1, result.Height));
    }

    public static Rect CalculateChainBounds(
        RouteChain chain,
        IReadOnlyDictionary<string, RouteNode> nodesById,
        IReadOnlyDictionary<string, RouteSegment> segmentsById)
    {
        var bounds = new List<Rect>();

        foreach (var nodeId in chain.NodeIds)
        {
            if (nodesById.TryGetValue(nodeId, out var node) && ContributesNodeBounds(node))
                bounds.Add(new Rect(node.X, node.Y, 0, 0));
        }

        foreach (var segmentId in chain.SegmentIds)
        {
            if (!segmentsById.TryGetValue(segmentId, out var segment) || !segment.IsVisible ||
                !nodesById.TryGetValue(segment.FromNodeId, out var from) ||
                !nodesById.TryGetValue(segment.ToNodeId, out var to))
                continue;

            AddSegmentBounds(bounds, segment, from, to);
        }

        return bounds.Count > 0
            ? RouteSegmentGeometry.UnionBounds(bounds)
            : new Rect(chain.X, chain.Y, 0, 0);
    }

    private static bool ContributesNodeBounds(RouteNode node) =>
        node.IsVisible && node.Kind != RouteNodeKind.TerminalAnchor && double.IsFinite(node.X) && double.IsFinite(node.Y);

    private static void AddSegmentBounds(List<Rect> bounds, RouteSegment segment, RouteNode from, RouteNode to)
    {
        var start = new Point(from.X, from.Y);
        var end = new Point(to.X, to.Y);
        if (segment.Kind != RouteSegmentKind.DeadEnd)
        {
            bounds.Add(RouteSegmentGeometry.Create(segment, start, end).Bounds);
            return;
        }

        var style = segment.Style ?? new RouteSegmentStyle();
        if (!RouteSegmentGeometry.TryCreateTerminalCap(segment, style, start, end, out var cap))
            return;

        // Both pens scale with the path, so logical stroke bounds also fit in view coordinates.
        var halfBodyStroke = Math.Max(style.Thickness, RouteSegmentGeometry.TerminalActiveThickness(style)) / 2;
        var bodyBounds = RouteSegmentGeometry.BoundsOf(start, end).Inflate(halfBodyStroke);
        var capBounds = RouteSegmentGeometry.BoundsOf(cap.Start, cap.End).Inflate(style.Thickness / 2);
        var segmentBounds = bodyBounds.Union(capBounds);
        if (double.IsFinite(segmentBounds.Right) && double.IsFinite(segmentBounds.Bottom) &&
            double.IsFinite(segmentBounds.Left) && double.IsFinite(segmentBounds.Top))
            bounds.Add(segmentBounds);
    }
}
