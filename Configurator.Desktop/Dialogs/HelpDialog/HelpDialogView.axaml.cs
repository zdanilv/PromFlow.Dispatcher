using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Configurator.Desktop.Dialogs.HelpDialog;

public partial class HelpDialogView : UserControl
{
    public HelpDialogView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
