using Avalonia.Markup.Xaml;
using ReactiveUI.Avalonia;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;

namespace Configurator.Desktop.Workspace.RouteMap.Panels;

public partial class TopBarView : ReactiveUserControl<TopBarViewModel>
{
    public TopBarView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
