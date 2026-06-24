using Configurator.Application.Services.Authorization;
using Configurator.Desktop.Dialogs;
using Microsoft.Extensions.DependencyInjection;

namespace Configurator.Desktop.Workspace.RouteMap.Settings;

public sealed class RouteMapSettingsDialogService(
    IServiceProvider serviceProvider,
    IAccessDecisionService accessDecisionService,
    DialogCoordinator dialogCoordinator) : IRouteMapSettingsDialogService
{
    public async Task ShowAsync(CancellationToken cancellationToken = default)
    {
        var decision = await accessDecisionService
            .AuthorizeAsync(new AccessRequirement(Permission.EditSignalMapping), cancellationToken);
        if (!decision.Succeeded)
        {
            throw new UnauthorizedAccessException(
                $"Edit signal mapping permission is required. Reason: {decision.ReasonCode}");
        }

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
