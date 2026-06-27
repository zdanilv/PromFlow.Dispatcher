using Avalonia;
using Avalonia.Markup.Xaml;
using ReactiveUI.Avalonia;

namespace Configurator.Desktop.Workspace.Archive;

public partial class ArchiveView : ReactiveUserControl<ArchiveViewModel>
{
    public ArchiveView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
