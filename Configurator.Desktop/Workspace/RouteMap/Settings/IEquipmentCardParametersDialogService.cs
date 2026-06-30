using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.Settings;

public interface IEquipmentCardParametersDialogService
{
    Task ShowAsync(
        EquipmentCommandCard card,
        IReadOnlyDictionary<string, SignalValue>? signals,
        CancellationToken cancellationToken = default);
}
