using Configurator.Desktop.Dialogs;
using Microsoft.Extensions.DependencyInjection;

namespace Configurator.Desktop.Dialogs.HelpDialog;

public sealed class HelpDialogService(
    IServiceProvider serviceProvider,
    DialogCoordinator dialogCoordinator) : IHelpDialogService
{
    public Task ShowAsync(CancellationToken cancellationToken = default)
    {
        var viewModel = serviceProvider.GetRequiredService<HelpDialogViewModel>();
        var view = serviceProvider.GetRequiredService<HelpDialogView>();
        view.DataContext = viewModel;
        return dialogCoordinator.ShowAsync(new DialogViewContext<bool>(view, viewModel.Result), cancellationToken);
    }
}
