using Configurator.Application.Services.Signals;
using Configurator.Application.Services;
using Configurator.Desktop.Dialogs;
using Configurator.Desktop.Dialogs.EquipmentCardParametersDialog;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Configurator.Desktop.Workspace.RouteMap.Settings;

public sealed class EquipmentCardParametersDialogService(
    IServiceProvider serviceProvider,
    DialogCoordinator dialogCoordinator,
    IOptions<ApplicationOptions> applicationOptions) : IEquipmentCardParametersDialogService
{
    public async Task ShowAsync(
        EquipmentCommandCard card,
        IReadOnlyDictionary<string, SignalValue>? signals,
        CancellationToken cancellationToken = default)
    {
        var snapshot = signals ?? new Dictionary<string, SignalValue>(StringComparer.Ordinal);
        var viewModel = ActivatorUtilities.CreateInstance<EquipmentCardParametersDialogViewModel>(
            serviceProvider,
            card,
            snapshot,
            applicationOptions.Value.IsAdminMode);
        var view = serviceProvider.GetRequiredService<EquipmentCardParametersDialogView>();
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
