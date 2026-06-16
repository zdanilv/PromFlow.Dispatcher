using Avalonia.Markup.Xaml;
using ReactiveUI.Avalonia;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;

namespace Configurator.Desktop.Workspace.RouteMap.Panels;

public partial class RequestsPanelView : ReactiveUserControl<RequestsPanelViewModel>
{
    public RequestsPanelView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
