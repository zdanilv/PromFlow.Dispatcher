using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.Settings;

public sealed class RouteMapEquipmentParameterValueStore(
    RouteMapConfigurationManager configurationManager) : IEquipmentParameterValueStore, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task SaveAsync(
        string cardId,
        IReadOnlyCollection<EquipmentParameterSetpoint> values,
        bool pendingAutoDispatch,
        CancellationToken cancellationToken = default)
    {
        if (values.Count == 0)
            return;

        await UpdateAsync(document =>
        {
            var card = FindCard(document, cardId);
            foreach (var value in values)
            {
                var parameter = FindParameter(card, value.SignalId);
                if (parameter.Direction is not (SignalBindingDirection.Write or SignalBindingDirection.ReadWrite))
                    throw new InvalidOperationException($"Параметр '{value.SignalId}' доступен только для чтения.");

                parameter.SavedValue = value.Value;
                parameter.PendingAutoDispatch = pendingAutoDispatch;
                parameter.LastDispatchError = null;
            }
        }, cancellationToken);
    }

    public IReadOnlyList<PendingEquipmentParameter> GetPending() =>
        configurationManager.CurrentDefinition.MapEquipment
            .SelectMany(card => card.Parameters
                .Where(parameter =>
                    parameter.Binding.Direction is SignalBindingDirection.Write or SignalBindingDirection.ReadWrite &&
                    parameter.PendingAutoDispatch &&
                    parameter.SavedValue is not null)
                .Select(parameter => new PendingEquipmentParameter(card.Id, parameter)))
            .ToArray();

    public Task CompleteDispatchAsync(
        string cardId,
        string signalId,
        string expectedSavedValue,
        string? errorMessage,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(document =>
        {
            var parameter = FindParameter(FindCard(document, cardId), signalId);
            if (!string.Equals(parameter.SavedValue, expectedSavedValue, StringComparison.Ordinal))
                return;

            parameter.PendingAutoDispatch = false;
            parameter.LastDispatchError = errorMessage;
        }, cancellationToken);

    public void Dispose() => _gate.Dispose();

    private async Task UpdateAsync(
        Action<RouteMapConfigurationDocument> update,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var draft = configurationManager.CreateDraft();
            update(draft);
            var result = await configurationManager.SaveAndApplyAsync(draft, cancellationToken);
            if (!result.IsSuccess)
            {
                var details = result.ErrorMessage ?? string.Join("; ", result.Errors.Select(error => error.Message));
                throw new InvalidOperationException($"Не удалось сохранить RouteMap: {details}");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static EquipmentCardConfiguration FindCard(RouteMapConfigurationDocument document, string cardId) =>
        document.Cards.FirstOrDefault(card => string.Equals(card.Id, cardId, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"Карточка '{cardId}' не найдена в активном RouteMap-профиле.");

    private static EquipmentCardParameterConfiguration FindParameter(
        EquipmentCardConfiguration card,
        string signalId) =>
        card.Parameters.FirstOrDefault(parameter => string.Equals(parameter.SignalId, signalId, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"Параметр '{signalId}' не найден в карточке '{card.Id}'.");
}
