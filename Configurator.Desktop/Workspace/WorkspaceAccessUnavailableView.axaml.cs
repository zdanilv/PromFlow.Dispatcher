using Avalonia.Markup.Xaml;
using ReactiveUI.Avalonia;

namespace Configurator.Desktop.Workspace;

public partial class WorkspaceAccessUnavailableView : ReactiveUserControl<WorkspaceAccessUnavailableViewModel>
{
    public WorkspaceAccessUnavailableView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
