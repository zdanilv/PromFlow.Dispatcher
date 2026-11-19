using Configurator.Application.Services;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Microsoft.Extensions.Options;

namespace Configurator.Desktop.Workspace.RouteMap.Settings;

public sealed class RouteMapEquipmentParameterValueStore(
    RouteMapConfigurationManager configurationManager,
    IOptions<ApplicationOptions> applicationOptions)
    : IEquipmentParameterValueStore, IEquipmentCardParameterDefinitionStore, IDisposable
{
    private bool _disposed;

    public RouteMapEquipmentParameterValueStore(RouteMapConfigurationManager configurationManager)
        : this(configurationManager, Options.Create(new ApplicationOptions { WorkMode = ApplicationOptions.UserWorkMode }))
    {
    }

    public async Task<EquipmentCardParameter> AddAsync(
        string cardId,
        NewEquipmentCardParameterDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!applicationOptions.Value.IsAdminMode)
            throw new InvalidOperationException("Добавлять настройки оборудования может только администратор.");

        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(cardId))
            throw new InvalidOperationException("Идентификатор карточки обязателен.");
        if (string.IsNullOrWhiteSpace(definition.Title))
            throw new InvalidOperationException("Название настройки оборудования обязательно.");
        if (string.IsNullOrWhiteSpace(definition.SignalId))
            throw new InvalidOperationException("SignalId настройки оборудования обязателен.");
        if (!Enum.IsDefined(definition.Direction))
            throw new InvalidOperationException("Недопустимое направление (Direction) настройки оборудования.");
        if (!Enum.IsDefined(definition.ValueType))
            throw new InvalidOperationException("Недопустимый тип (ValueType) настройки оборудования.");
        if (!Enum.IsDefined(definition.Visibility))
            throw new InvalidOperationException("Недопустимая видимость (Visibility) настройки оборудования.");

        var signalId = definition.SignalId.Trim();
        var applied = await configurationManager.UpdateAsync(document =>
        {
            if (configurationManager.LastLoadError is { } loadError)
                throw new InvalidOperationException(loadError);

            var card = FindCard(document, cardId);
            if (card.Parameters.Any(parameter =>
                    string.Equals(parameter.SignalId, signalId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Настройка оборудования с SignalId '{signalId}' уже существует в карточке '{cardId}'.");
            }

            card.Parameters.Add(new EquipmentCardParameterConfiguration
            {
                Title = definition.Title.Trim(),
                SignalId = signalId,
                Role = SignalBindingRole.EquipmentParameter,
                Direction = definition.Direction,
                ValueType = definition.ValueType,
                Visibility = definition.Visibility,
                RequiresExplicitSetpoint = true,
            });
        }, cancellationToken);

        return applied.MapEquipment.Single(card => card.Id == cardId)
            .Parameters.Single(parameter => parameter.Binding.SignalId == signalId);
    }

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

    public Task DeferDispatchAsync(
        string cardId,
        string signalId,
        string expectedSavedValue,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(document =>
        {
            var parameter = FindParameter(FindCard(document, cardId), signalId);
            if (!string.Equals(parameter.SavedValue, expectedSavedValue, StringComparison.Ordinal))
                return;

            parameter.PendingAutoDispatch = true;
            parameter.LastDispatchError = null;
        }, cancellationToken);

    public void Dispose() => _disposed = true;

    private Task UpdateAsync(
        Action<RouteMapConfigurationDocument> update,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return configurationManager.UpdateAsync(update, cancellationToken);
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
