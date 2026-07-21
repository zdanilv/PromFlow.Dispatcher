using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.Settings;

public sealed record EquipmentParameterSetpoint(
    string SignalId,
    string Value);

public sealed record PendingEquipmentParameter(
    string CardId,
    EquipmentCardParameter Parameter);

public interface IEquipmentParameterValueStore
{
    Task SaveAsync(
        string cardId,
        IReadOnlyCollection<EquipmentParameterSetpoint> values,
        bool pendingAutoDispatch,
        CancellationToken cancellationToken = default);

    IReadOnlyList<PendingEquipmentParameter> GetPending();

    Task CompleteDispatchAsync(
        string cardId,
        string signalId,
        string expectedSavedValue,
        string? errorMessage,
        CancellationToken cancellationToken = default);
}
