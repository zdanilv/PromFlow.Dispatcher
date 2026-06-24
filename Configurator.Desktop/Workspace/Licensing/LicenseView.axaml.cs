using Avalonia.Markup.Xaml;
using ReactiveUI.Avalonia;

namespace Configurator.Desktop.Workspace.Licensing;

public partial class LicenseView : ReactiveUserControl<LicenseViewModel>
{
    public LicenseView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
