using Configurator.Application.Services.Signals;
using Configurator.Desktop.Dialogs.EquipmentCardParametersDialog;
using Configurator.Desktop.Workspace.RouteMap.Services;

namespace Configurator.Desktop.Workspace.RouteMap.Settings;

public sealed class EquipmentParameterAutoDispatcher(
    IEquipmentParameterValueStore valueStore,
    IEquipmentCommandDispatcher dispatcher,
    IEquipmentParameterWriteValidator writeValidator,
    IRouteMapSignalRuntime routeMapSignalRuntime) : IEquipmentParameterAutoDispatcher
{
    public async Task DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        // Keep the constructor contract, but never choose a backend independently of its generation.
        _ = dispatcher;
        if (routeMapSignalRuntime is not RouteMapSignalRuntime runtime ||
            !runtime.TryGetAvailableModbusGeneration(out var generation))
            return;

        var gate = runtime.EquipmentParameterDispatchGate;
        await gate.WaitAsync(cancellationToken);
        try
        {
            await DispatchCoreAsync(runtime, generation, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task DispatchCoreAsync(RouteMapSignalRuntime runtime, long generation,
        CancellationToken cancellationToken)
    {
        bool IsBatchAvailable() => runtime.TryGetAvailableModbusGeneration(out var current) && current == generation;
        if (!IsBatchAvailable())
            return;

        foreach (var pending in valueStore.GetPending())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsBatchAvailable())
                break;
            if (!valueStore.GetPending().Any(current => current.CardId == pending.CardId &&
                    current.Parameter.Binding.SignalId == pending.Parameter.Binding.SignalId &&
                    current.Parameter.SavedValue == pending.Parameter.SavedValue))
                continue;
            var row = new EquipmentCardParameterRow(pending.Parameter, null);
            if (!row.TryCreateRequest(out var request, out var validationError))
            {
                if (!IsBatchAvailable())
                    break;
                await CompleteAsync(pending, validationError ?? "Сохранённое значение параметра некорректно.", cancellationToken);
                continue;
            }

            var writeError = writeValidator.Validate(request);
            if (!string.IsNullOrWhiteSpace(writeError))
            {
                if (!IsBatchAvailable())
                    break;
                await CompleteAsync(pending, writeError, cancellationToken);
                continue;
            }

            if (!IsBatchAvailable())
                break;
            // Consume the single automatic attempt before transport I/O. If saving
            // its outcome later fails, reconnect/restart must never repeat the write.
            if (!await valueStore.TryBeginDispatchAsync(pending.CardId, request.SignalId,
                    pending.Parameter.SavedValue!, cancellationToken))
                continue;

            string? dispatchError = null;
            OperationCanceledException? cancellation = null;
            bool dispatched;
            try
            {
                dispatched = await runtime.TryDispatchEquipmentParameterAsync(generation, request, cancellationToken);
            }
            catch (Exception ex)
            {
                dispatched = true;
                dispatchError = string.IsNullOrWhiteSpace(ex.Message)
                    ? "Отправка отменена; результат неизвестен." : ex.Message;
                cancellation = ex as OperationCanceledException;
            }

            if (!dispatched)
            {
                await valueStore.DeferDispatchAsync(pending.CardId, request.SignalId,
                    pending.Parameter.SavedValue!, CancellationToken.None);
                cancellationToken.ThrowIfCancellationRequested();
                break;
            }

            // Only a Modbus invocation reaches here. Its known result still belongs to that
            // attempt after a source switch; expectedSavedValue protects a newer local edit.
            var isBatchAvailable = IsBatchAvailable();
            await CompleteAsync(pending, dispatchError, CancellationToken.None);
            if (cancellation is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cancellation).Throw();
            if (!isBatchAvailable || !IsBatchAvailable())
                break;
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
