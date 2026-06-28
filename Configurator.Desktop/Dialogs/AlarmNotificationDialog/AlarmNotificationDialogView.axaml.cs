using ReactiveUI;
using ReactiveUI.Avalonia;

namespace Configurator.Desktop.Dialogs.AlarmNotificationDialog;

public partial class AlarmNotificationDialogView : ReactiveUserControl<AlarmNotificationDialogViewModel>
{
    public AlarmNotificationDialogView()
    {
        InitializeComponent();
    }
}
