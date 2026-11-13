using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.Settings;

public sealed record NewEquipmentCardParameterDefinition(
    string Title,
    string SignalId,
    SignalBindingDirection Direction,
    SignalValueType ValueType,
    EquipmentParameterVisibility Visibility);

public interface IEquipmentCardParameterDefinitionStore
{
    Task<EquipmentCardParameter> AddAsync(
        string cardId,
        NewEquipmentCardParameterDefinition definition,
        CancellationToken cancellationToken = default);
}
