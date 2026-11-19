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

    // Return true only after durably consuming the matching pending attempt.
    // Stores without this guarantee must not start automatic physical writes.
    Task<bool> TryBeginDispatchAsync(
        string cardId,
        string signalId,
        string expectedSavedValue,
        CancellationToken cancellationToken = default) => Task.FromResult(false);

    Task DeferDispatchAsync(
        string cardId,
        string signalId,
        string expectedSavedValue,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("Хранилище не поддерживает отложенную отправку."));

    Task CompleteDispatchAsync(
        string cardId,
        string signalId,
        string expectedSavedValue,
        string? errorMessage,
        CancellationToken cancellationToken = default);
}
