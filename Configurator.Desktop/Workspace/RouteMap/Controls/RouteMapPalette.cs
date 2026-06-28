using Avalonia.Media;
using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.Controls;

public static class RouteMapPalette
{
    public static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#F7F8F8"));
    public static readonly IBrush PanelBrush = new SolidColorBrush(Color.Parse("#FFFFFF"));
    public static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#44505C"));
    public static readonly IBrush MutedTextBrush = new SolidColorBrush(Color.Parse("#77828D"));
    public static readonly IBrush TrackBrush = new SolidColorBrush(Color.Parse("#C9CED0"));
    public static readonly IBrush TrackActiveBrush = new SolidColorBrush(Color.Parse("#2D56B3"));
    public static readonly IBrush ReadyBrush = new SolidColorBrush(Color.Parse("#3A9D5D"));
    public static readonly IBrush RunningBrush = new SolidColorBrush(Color.Parse("#2563EB"));
    public static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#D99B22"));
    public static readonly IBrush FaultBrush = new SolidColorBrush(Color.Parse("#D95D4E"));
    public static readonly IBrush OfflineBrush = new SolidColorBrush(Color.Parse("#3F474D"));
    public static readonly IBrush DisabledBrush = new SolidColorBrush(Color.Parse("#D8DCDF"));
    public static readonly IBrush NodeFillBrush = new SolidColorBrush(Color.Parse("#AEB5BA"));
    public static readonly IBrush SelectionBrush = new SolidColorBrush(Color.Parse("#21428E"));
    public static readonly IBrush HoverBrush = new SolidColorBrush(Color.Parse("#1E6BFF"));

    public static readonly Pen TrackPen = new(TrackBrush, 4);
    public static readonly Pen TrackActivePen = new(TrackActiveBrush, 5);
    public static readonly Pen TrackDisabledPen = new(DisabledBrush, 4);
    public static readonly Pen NodeBorderPen = new(new SolidColorBrush(Color.Parse("#DDE1E4")), 2);
    public static readonly Pen SelectionPen = new(SelectionBrush, 3);
    public static readonly Pen HoverPen = new(HoverBrush, 2);

    public static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));

    public static IBrush BrushForState(RouteObjectState state, RouteMapPaletteSettings palette, string? idleColor = null)
    {
        return Brush(state switch
        {
            RouteObjectState.Ready => palette.Ready,
            RouteObjectState.Running => palette.Running,
            RouteObjectState.ActiveRoute => palette.ActiveTrack,
            RouteObjectState.Warning => palette.Warning,
            RouteObjectState.Fault => palette.Fault,
            RouteObjectState.Disabled => palette.Disabled,
            RouteObjectState.Offline => palette.Offline,
            _ => idleColor ?? palette.NodeFill,
        });
    }

    public static Pen TrackPenForState(
        RouteObjectState state,
        RouteMapPaletteSettings palette,
        RouteSegmentStyle style)
    {
        var color = state switch
        {
            RouteObjectState.ActiveRoute or RouteObjectState.Running => style.ActiveColor,
            RouteObjectState.Disabled => palette.Disabled,
            RouteObjectState.Offline => palette.Offline,
            RouteObjectState.Fault => palette.Fault,
            RouteObjectState.Warning => palette.Warning,
            _ => style.NormalColor,
        };
        var thickness = state is RouteObjectState.ActiveRoute or RouteObjectState.Running
            ? style.ActiveThickness
            : style.Thickness;
        var lineCap = style.LineCap == RouteLineCap.Round
            ? PenLineCap.Round
            : PenLineCap.Flat;
        return new Pen(Brush(color), thickness, null, lineCap, PenLineJoin.Round, 10);
    }

    public static IBrush BrushForState(RouteObjectState state)
    {
        return state switch
        {
            RouteObjectState.Ready => ReadyBrush,
            RouteObjectState.Running => RunningBrush,
            RouteObjectState.ActiveRoute => TrackActiveBrush,
            RouteObjectState.Warning => WarningBrush,
            RouteObjectState.Fault => FaultBrush,
            RouteObjectState.Disabled => DisabledBrush,
            RouteObjectState.Offline => OfflineBrush,
            _ => NodeFillBrush,
        };
    }

    public static Pen TrackPenForState(RouteObjectState state)
    {
        return state switch
        {
            RouteObjectState.ActiveRoute => TrackActivePen,
            RouteObjectState.Running => TrackActivePen,
            RouteObjectState.Disabled => TrackDisabledPen,
            RouteObjectState.Offline => new Pen(OfflineBrush, 4),
            RouteObjectState.Fault => new Pen(FaultBrush, 5),
            RouteObjectState.Warning => new Pen(WarningBrush, 5),
            _ => TrackPen,
        };
    }
}
