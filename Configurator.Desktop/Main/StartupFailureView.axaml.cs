using Avalonia.Markup.Xaml;
using ReactiveUI.Avalonia;

namespace Configurator.Desktop.Main;

public partial class StartupFailureView : ReactiveUserControl<StartupFailureViewModel>
{
    public StartupFailureView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
