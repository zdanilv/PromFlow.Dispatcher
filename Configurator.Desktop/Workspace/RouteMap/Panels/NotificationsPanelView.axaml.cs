using Avalonia.Markup.Xaml;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;
using ReactiveUI.Avalonia;

namespace Configurator.Desktop.Workspace.RouteMap.Panels;

public partial class NotificationsPanelView : ReactiveUserControl<NotificationsPanelViewModel>
{
    public NotificationsPanelView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
