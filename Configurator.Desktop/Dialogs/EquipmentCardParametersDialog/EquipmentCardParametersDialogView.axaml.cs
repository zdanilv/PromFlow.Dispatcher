using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ReactiveUI.Avalonia;

namespace Configurator.Desktop.Dialogs.EquipmentCardParametersDialog;

public partial class EquipmentCardParametersDialogView : ReactiveUserControl<EquipmentCardParametersDialogViewModel>
{
    public EquipmentCardParametersDialogView()
    {
        InitializeComponent();
    }

    private void FocusNewParameterTitle(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (NewParameterTitleBox.Focus())
                NewParameterTitleBox.BringIntoView();
        }, DispatcherPriority.Loaded);
    }
}
