using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.Controls;

public enum RouteMapLegendGlyphKind
{
    SelectedNode,
    HoveredNode,
    ActiveNode,
    TargetNode,
    LoaderNode,
    FaultNode,
    OfflineNode,
    ActiveSegment,
    FaultSegment,
    OfflineSegment,
    DeadEnd
}

public sealed class RouteMapLegendGlyph : Control
{
    private const double DesignWidth = 32;
    private const double DesignHeight = 24;

    public static readonly StyledProperty<RouteMapDefinition?> DefinitionProperty =
        AvaloniaProperty.Register<RouteMapLegendGlyph, RouteMapDefinition?>(nameof(Definition));

    public static readonly StyledProperty<RouteMapLegendGlyphKind> KindProperty =
        AvaloniaProperty.Register<RouteMapLegendGlyph, RouteMapLegendGlyphKind>(nameof(Kind));

    static RouteMapLegendGlyph()
    {
        AffectsRender<RouteMapLegendGlyph>(DefinitionProperty, KindProperty);
    }

    public RouteMapLegendGlyph()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    public RouteMapDefinition? Definition
    {
        get => GetValue(DefinitionProperty);
        set => SetValue(DefinitionProperty, value);
    }

    public RouteMapLegendGlyphKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var display = Definition?.Display ?? new RouteMapDisplaySettings();
        var palette = display.Palette;
        var nodeStyle = Definition?.Nodes.FirstOrDefault(node => node.Kind != RouteNodeKind.TerminalAnchor)?.Style ?? new RouteNodeStyle();
        var segmentStyle = Definition?.Segments.FirstOrDefault()?.Style ?? new RouteSegmentStyle();
        var scale = Math.Min(Bounds.Width / DesignWidth, Bounds.Height / DesignHeight);

        if (Kind == RouteMapLegendGlyphKind.DeadEnd)
        {
            DrawDeadEnd(context, palette, scale);
            return;
        }

        if (Kind is RouteMapLegendGlyphKind.ActiveSegment
            or RouteMapLegendGlyphKind.FaultSegment
            or RouteMapLegendGlyphKind.OfflineSegment)
        {
            DrawSegment(context, palette, segmentStyle, scale);
            return;
        }

        DrawNode(context, palette, nodeStyle, scale);
    }

    private void DrawNode(
        DrawingContext context,
        RouteMapPaletteSettings palette,
        RouteNodeStyle style,
        double scale)
    {
        var radius = 5 * scale;
        var center = new Point(14 * scale, 12 * scale);
        var fill = Kind switch
        {
            RouteMapLegendGlyphKind.ActiveNode => RouteMapPalette.ActiveNodeBrush,
            RouteMapLegendGlyphKind.TargetNode => RouteMapPalette.Brush(palette.Ready),
            RouteMapLegendGlyphKind.LoaderNode => RouteMapPalette.Brush(palette.Warning),
            RouteMapLegendGlyphKind.FaultNode => RouteMapPalette.Brush(palette.Fault),
            RouteMapLegendGlyphKind.OfflineNode => RouteMapPalette.Brush(palette.Offline),
            _ => RouteMapPalette.Brush(style.FillColor)
        };

        context.DrawEllipse(
            fill,
            new Pen(RouteMapPalette.Brush(style.BorderColor), style.BorderThickness * scale),
            center,
            radius,
            radius);
        if (style.InnerRadiusRatio > 0)
        {
            var innerBrush = Kind == RouteMapLegendGlyphKind.OfflineNode
                ? fill
                : RouteMapPalette.Brush(style.InnerColor);
            var innerRadius = radius * style.InnerRadiusRatio;
            context.DrawEllipse(innerBrush, null, center, innerRadius, innerRadius);
        }

        switch (Kind)
        {
            case RouteMapLegendGlyphKind.SelectedNode:
                context.DrawEllipse(
                    null,
                    new Pen(RouteMapPalette.Brush(palette.Selection), 3 * scale),
                    center,
                    radius + 6 * scale,
                    radius + 6 * scale);
                break;
            case RouteMapLegendGlyphKind.HoveredNode:
                context.DrawEllipse(
                    null,
                    new Pen(RouteMapPalette.Brush(palette.Hover), 2 * scale),
                    center,
                    radius + 3 * scale,
                    radius + 3 * scale);
                break;
        }
    }

    private void DrawDeadEnd(DrawingContext context, RouteMapPaletteSettings palette, double scale)
    {
        var terminal = Definition?.Segments.FirstOrDefault(segment => segment.Kind == RouteSegmentKind.DeadEnd);
        var style = terminal?.Style ?? (terminal is null ? RouteMapSeed.StandardSegmentStyle : new RouteSegmentStyle());
        var thickness = Math.Min(style.Thickness, DesignHeight / 2);
        var capLength = Math.Min(terminal?.TerminalCapLength ?? 7 * style.Thickness, DesignHeight - 2 - thickness);
        var start = new Point(3 * scale, 12 * scale);
        var end = new Point(27 * scale, 12 * scale);
        var scaledStyle = style with { Thickness = thickness * scale };
        if (!RouteSegmentGeometry.TryCreateTerminalCap(
                RouteTerminalSide.To, capLength * scale, scaledStyle.Thickness, start, end, out var cap))
            return;

        var pen = RouteMapPalette.TrackPenForState(RouteObjectState.Idle, palette, scaledStyle);
        context.DrawLine(pen, start, end);
        context.DrawLine(pen, cap.Start, cap.End);
    }

    private void DrawSegment(
        DrawingContext context,
        RouteMapPaletteSettings palette,
        RouteSegmentStyle style,
        double scale)
    {
        var start = new Point(3 * scale, 12 * scale);
        var end = new Point(29 * scale, 12 * scale);

        if (Kind is RouteMapLegendGlyphKind.FaultSegment or RouteMapLegendGlyphKind.OfflineSegment)
        {
            var state = Kind == RouteMapLegendGlyphKind.FaultSegment
                ? RouteObjectState.Fault
                : RouteObjectState.Offline;
            context.DrawLine(
                ScalePen(RouteMapPalette.TrackPenForState(state, palette, style), scale),
                start,
                end);
            return;
        }

        context.DrawLine(
            ScalePen(RouteMapPalette.TrackPenForState(RouteObjectState.Idle, palette, style), scale),
            start,
            end);
        context.DrawLine(
            ScalePen(RouteMapPalette.TrackPenForState(RouteObjectState.ActiveRoute, palette, style), scale),
            start,
            end);
    }

    private static Pen ScalePen(Pen pen, double scale) =>
        new(pen.Brush, pen.Thickness * scale, pen.DashStyle, pen.LineCap, pen.LineJoin, pen.MiterLimit);
}
