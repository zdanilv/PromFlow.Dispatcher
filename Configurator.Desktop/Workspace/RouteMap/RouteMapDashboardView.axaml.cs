using Avalonia.Markup.Xaml;
using ReactiveUI.Avalonia;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;

namespace Configurator.Desktop.Workspace.RouteMap;

public partial class RouteMapDashboardView : ReactiveUserControl<RouteMapDashboardViewModel>
{
    public RouteMapDashboardView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
