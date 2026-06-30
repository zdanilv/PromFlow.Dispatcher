using Configurator.Application.Services.Signals;
using Configurator.Desktop.Dialogs;
using Configurator.Desktop.Dialogs.EquipmentCardParametersDialog;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Configurator.Desktop.Workspace.RouteMap.Settings;

public sealed class EquipmentCardParametersDialogService(
    IServiceProvider serviceProvider,
    DialogCoordinator dialogCoordinator) : IEquipmentCardParametersDialogService
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
            snapshot);
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
