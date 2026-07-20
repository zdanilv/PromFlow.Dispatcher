namespace Configurator.Desktop.Dialogs.HelpDialog;

public interface IHelpDialogService
{
    Task ShowAsync(CancellationToken cancellationToken = default);
}
