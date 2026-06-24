using Avalonia.Markup.Xaml;
using ReactiveUI.Avalonia;

namespace Configurator.Desktop.Workspace.Authorization;

public partial class AdminBootstrapView : ReactiveUserControl<AdminBootstrapViewModel>
{
    public AdminBootstrapView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
