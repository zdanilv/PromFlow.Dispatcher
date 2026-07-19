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
    OfflineSegment
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
        var nodeStyle = Definition?.Nodes.FirstOrDefault()?.Style ?? new RouteNodeStyle();
        var segmentStyle = Definition?.Segments.FirstOrDefault()?.Style ?? new RouteSegmentStyle();
        var scale = Math.Min(Bounds.Width / DesignWidth, Bounds.Height / DesignHeight);

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
        var innerBrush = Kind == RouteMapLegendGlyphKind.OfflineNode
            ? fill
            : RouteMapPalette.Brush(style.InnerColor);
        context.DrawEllipse(
            innerBrush,
            null,
            center,
            radius * style.InnerRadiusRatio,
            radius * style.InnerRadiusRatio);

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
            case RouteMapLegendGlyphKind.ActiveNode:
                var outlineRadius = radius + (style.ActiveOutlineThickness / 2 + 1) * scale;
                context.DrawEllipse(
                    null,
                    new Pen(RouteMapPalette.Brush(style.ActiveOutlineColor), style.ActiveOutlineThickness * scale),
                    center,
                    outlineRadius,
                    outlineRadius);
                break;
        }
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
