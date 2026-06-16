using Configurator.Desktop.Dialogs;
using Microsoft.Extensions.DependencyInjection;

namespace Configurator.Desktop.Workspace.RouteMap.Settings;

public sealed class RouteMapSettingsDialogService(
    IServiceProvider serviceProvider,
    DialogCoordinator dialogCoordinator) : IRouteMapSettingsDialogService
{
    public async Task ShowAsync(CancellationToken cancellationToken = default)
    {
        var viewModel = serviceProvider.GetRequiredService<RouteMapSettingsViewModel>();
        var view = serviceProvider.GetRequiredService<RouteMapSettingsDialog>();
        view.DataContext = viewModel;
        try
        {
            await dialogCoordinator.ShowAsync(new DialogViewContext<bool>(view, viewModel.Result), cancellationToken);
        }
        finally
        {
            viewModel.Dispose();
        }
    }
}
