using Configurator.Application.Services.Signals;
using Configurator.Desktop.Dialogs.EquipmentCardParametersDialog;

namespace Configurator.Desktop.Workspace.RouteMap.Settings;

public sealed class EquipmentParameterAutoDispatcher(
    IEquipmentParameterValueStore valueStore,
    IEquipmentCommandDispatcher dispatcher,
    IEquipmentParameterWriteValidator writeValidator,
    IRouteMapSignalRuntime routeMapSignalRuntime) : IEquipmentParameterAutoDispatcher
{
    public async Task DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        if (routeMapSignalRuntime.CurrentSource != RouteMapSignalSource.Modbus)
            return;

        foreach (var pending in valueStore.GetPending())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = new EquipmentCardParameterRow(pending.Parameter, null);
            if (!row.TryCreateRequest(out var request, out var validationError))
            {
                await CompleteAsync(pending, validationError ?? "Сохранённое значение параметра некорректно.", cancellationToken);
                continue;
            }

            var writeError = writeValidator.Validate(request);
            if (!string.IsNullOrWhiteSpace(writeError))
            {
                await CompleteAsync(pending, writeError, cancellationToken);
                continue;
            }

            try
            {
                await dispatcher.DispatchAsync(request, cancellationToken);
                await CompleteAsync(pending, null, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await CompleteAsync(pending, ex.Message, cancellationToken);
            }
        }
    }

    private Task CompleteAsync(
        PendingEquipmentParameter pending,
        string? errorMessage,
        CancellationToken cancellationToken) =>
        valueStore.CompleteDispatchAsync(
            pending.CardId,
            pending.Parameter.Binding.SignalId,
            pending.Parameter.SavedValue!,
            errorMessage,
            cancellationToken);
}
