using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using ReactiveUI.Avalonia;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;

namespace Configurator.Desktop.Workspace.RouteMap;

public partial class RouteMapDashboardView : ReactiveUserControl<RouteMapDashboardViewModel>
{
    public RouteMapDashboardView()
    {
        AvaloniaXamlLoader.Load(this);
        var map = this.FindControl<RouteMapControl>("RouteMapCanvas")!;
        var legend = this.FindControl<Border>("RouteMapStateLegendOverlay")!;
        var connectionStatus = this.FindControl<Border>("ConnectionStatusOverlay")!;
        void UpdateLegendHeight() => Dispatcher.UIThread.Post(() => FitLegendAboveRoute(map, legend), DispatcherPriority.Loaded);
        map.SizeChanged += (_, _) => UpdateLegendHeight();
        connectionStatus.SizeChanged += (_, _) => UpdateLegendHeight();
        map.PropertyChanged += (_, change) =>
        {
            if (change.Property == RouteMapControl.DefinitionProperty || change.Property == RouteMapControl.MeasuredCardHeightProperty)
                UpdateLegendHeight();
        };
    }

    private static void FitLegendAboveRoute(RouteMapControl map, Border legend)
    {
        if (map.Definition is not { } definition || map.Bounds.Width <= 0 ||
            legend.TranslatePoint(new Point(legend.Bounds.Width, 0), map) is not { } legendTopRight)
            return;

        var layout = map.CreateViewportLayout(definition);
        var topLeft = layout.Transform.ToViewPoint(layout.LogicalRouteBounds.X, layout.LogicalRouteBounds.Y);
        // Circles and labels use DIP sizes, independent of the route scale.
        const double clearance = 26;
        var height = legendTopRight.X + clearance < topLeft.X
            ? double.PositiveInfinity
            : Math.Max(0, topLeft.Y - clearance - legendTopRight.Y);
        if (legend.MaxHeight != height)
            legend.MaxHeight = height;
    }
}
