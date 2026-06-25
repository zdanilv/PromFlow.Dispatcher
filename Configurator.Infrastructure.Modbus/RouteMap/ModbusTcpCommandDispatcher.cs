using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Runtime;
using Configurator.Application.Services.Signals;
using Configurator.Infrastructure.Modbus.Archiving;
using Microsoft.Extensions.Options;

namespace Configurator.Infrastructure.Modbus.RouteMap;

public sealed class ModbusTcpCommandDispatcher(
    IModbusTcpService modbusService,
    IOptionsMonitor<ModbusOptions> optionsMonitor,
    CommandAuditRecorder auditRecorder,
    ICommandDeliveryGate deliveryGate) : IEquipmentCommandDispatcher
{
    public async Task DispatchAsync(
        SignalWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        var options = optionsMonitor.CurrentValue;
        var point = options.DataMap.FirstOrDefault(
            candidate => string.Equals(candidate.Name, request.SignalId, StringComparison.OrdinalIgnoreCase));
        var context = auditRecorder.CreateContext(request, point?.WriteMode);
        var requestedAuditResult = await auditRecorder
            .RecordRequestedAsync(context, cancellationToken)
            .ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
        {
            await RecordCancelledAsync(context).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (auditRecorder.ShouldBlockCommandDelivery(context, requestedAuditResult))
        {
            await RecordRejectedAsync(
                context,
                requestedAuditResult.ErrorCode ?? "CommandAuditUnavailable",
                requestedAuditResult.ErrorMessage ?? "Command audit is unavailable.").ConfigureAwait(false);
            throw new InvalidOperationException("Command audit is unavailable.");
        }

        var deliveryDecision = deliveryGate.Evaluate(context);
        if (!deliveryDecision.Allowed)
        {
            await RecordRejectedAsync(
                context,
                deliveryDecision.ErrorCode ?? RuntimeCommandDeliveryGate.ShuttingDownErrorCode,
                deliveryDecision.ErrorMessage ?? "Command delivery is not allowed.").ConfigureAwait(false);
            throw new InvalidOperationException(deliveryDecision.ErrorMessage ?? "Command delivery is not allowed.");
        }

        if (point is null)
        {
            await RecordRejectedAsync(
                context,
                "ModbusDataPointMissing",
                $"Modbus signal '{request.SignalId}' is not configured.").ConfigureAwait(false);
            throw new InvalidOperationException($"Modbus signal '{request.SignalId}' is not configured.");
        }

        if (!point.IsWritable)
        {
            await RecordRejectedAsync(
                context,
                "ModbusDataPointNotWritable",
                $"Modbus signal '{request.SignalId}' is read-only.").ConfigureAwait(false);
            throw new InvalidOperationException($"Modbus signal '{request.SignalId}' is read-only.");
        }

        if (!IsCompatible(point.Type, request.ValueType))
        {
            await RecordRejectedAsync(
                context,
                "ModbusDataPointTypeMismatch",
                $"Signal '{request.SignalId}' expects {point.Type}, but RouteMap requested {request.ValueType}.")
                .ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Signal '{request.SignalId}' expects {point.Type}, but RouteMap requested {request.ValueType}.");
        }

        if (point.WriteMode == ModbusWriteMode.Pulse)
        {
            if (request.Value is not bool pulseRequested || !pulseRequested)
            {
                await auditRecorder.RecordTerminalAsync(
                    context,
                    EquipmentCommandAuditResult.Succeeded,
                    errorCode: null,
                    errorMessage: null,
                    CommandConfirmationStatus.NotApplicable,
                    confirmedAtUtc: null,
                    CancellationToken.None).ConfigureAwait(false);
                return;
            }

            await DispatchPulseAsync(point, context, cancellationToken).ConfigureAwait(false);
            return;
        }

        await DispatchSingleWriteAsync(point, request.Value, context, cancellationToken).ConfigureAwait(false);
    }

    private async Task DispatchSingleWriteAsync(
        ModbusDataPointOptions point,
        object? value,
        CommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await modbusService
                .SetAsync(point.Name, value, context, cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                await RecordModbusFailureAsync(context, result).ConfigureAwait(false);
                throw CreateWriteException(result);
            }

            var confirmationStatus = GetSuccessConfirmationStatus(point, optionsMonitor.CurrentValue);
            await auditRecorder.RecordTerminalAsync(
                context,
                EquipmentCommandAuditResult.Succeeded,
                errorCode: null,
                errorMessage: null,
                confirmationStatus,
                confirmationStatus == CommandConfirmationStatus.Confirmed ? DateTimeOffset.UtcNow : null,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await RecordCancelledAsync(context).ConfigureAwait(false);
            throw;
        }
    }

    private async Task DispatchPulseAsync(
        ModbusDataPointOptions point,
        CommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var setResult = await modbusService
                .SetAsync(point.Name, true, context, cancellationToken)
                .ConfigureAwait(false);
            if (!setResult.Succeeded)
            {
                await RecordModbusFailureAsync(context, setResult).ConfigureAwait(false);
                throw CreateWriteException(setResult);
            }

            OperationCanceledException? delayCancellation = null;
            try
            {
                await Task.Delay(point.PulseDurationMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                delayCancellation = ex;
            }

            using var resetTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            ModbusOperationResult resetResult;
            try
            {
                resetResult = await modbusService
                    .SetAsync(point.Name, false, context, resetTimeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                await auditRecorder.RecordTerminalAsync(
                    context,
                    EquipmentCommandAuditResult.Failed,
                    "ModbusPulseResetCanceled",
                    "Pulse reset Modbus write was canceled.",
                    CommandConfirmationStatus.ConnectionLost,
                    confirmedAtUtc: null,
                    CancellationToken.None).ConfigureAwait(false);
                throw new InvalidOperationException("Pulse reset Modbus write was canceled.", ex);
            }

            if (!resetResult.Succeeded)
            {
                if (delayCancellation is not null)
                {
                    throw delayCancellation;
                }

                await RecordModbusFailureAsync(context, resetResult).ConfigureAwait(false);
                throw CreateWriteException(resetResult);
            }

            if (delayCancellation is not null)
            {
                throw delayCancellation;
            }

            var confirmationStatus = GetSuccessConfirmationStatus(point, optionsMonitor.CurrentValue);
            await auditRecorder.RecordTerminalAsync(
                context,
                EquipmentCommandAuditResult.Succeeded,
                errorCode: null,
                errorMessage: null,
                confirmationStatus,
                confirmationStatus == CommandConfirmationStatus.Confirmed ? DateTimeOffset.UtcNow : null,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await RecordCancelledAsync(context).ConfigureAwait(false);
            throw;
        }
    }

    private async Task RecordRejectedAsync(
        CommandExecutionContext context,
        string errorCode,
        string errorMessage)
        => await auditRecorder.RecordTerminalAsync(
            context,
            EquipmentCommandAuditResult.Failed,
            errorCode,
            errorMessage,
            CommandConfirmationStatus.Rejected,
            confirmedAtUtc: null,
            CancellationToken.None).ConfigureAwait(false);

    private async Task RecordCancelledAsync(CommandExecutionContext context)
        => await auditRecorder.RecordTerminalAsync(
            context,
            EquipmentCommandAuditResult.Cancelled,
            "ModbusCommandCanceled",
            "Modbus command was canceled.",
            CommandConfirmationStatus.NotApplicable,
            confirmedAtUtc: null,
            CancellationToken.None).ConfigureAwait(false);

    private async Task RecordModbusFailureAsync(
        CommandExecutionContext context,
        ModbusOperationResult result)
        => await auditRecorder.RecordTerminalAsync(
            context,
            EquipmentCommandAuditResult.Failed,
            result.ErrorCode,
            result.ErrorMessage,
            GetFailureConfirmationStatus(result),
            confirmedAtUtc: null,
            CancellationToken.None).ConfigureAwait(false);

    private static CommandConfirmationStatus GetSuccessConfirmationStatus(
        ModbusDataPointOptions point,
        ModbusOptions options)
        => point.IsReadable && options.WriteConfirmationTimeoutMs > 0
            ? CommandConfirmationStatus.Confirmed
            : CommandConfirmationStatus.NotApplicable;

    private static CommandConfirmationStatus GetFailureConfirmationStatus(ModbusOperationResult result)
        => result.ErrorCode switch
        {
            "ModbusWriteConfirmationTimeout" => CommandConfirmationStatus.TimedOut,
            "ModbusDataPointMissing"
                or "ModbusDataPointNotWritable"
                or "ModbusDataPointTypeMismatch"
                or "ModbusRegisterShadowUnavailable"
                or "ModbusValueEncodeFailed" => CommandConfirmationStatus.Rejected,
            _ => CommandConfirmationStatus.ConnectionLost
        };

    private static InvalidOperationException CreateWriteException(ModbusOperationResult result)
        => new(
            result.ErrorDetails is null
                ? result.ErrorMessage
                : $"{result.ErrorMessage} {result.ErrorDetails}");

    private static bool IsCompatible(ModbusValueType modbusType, SignalValueType signalType)
    {
        return (modbusType, signalType) switch
        {
            (ModbusValueType.Bool, SignalValueType.Bool) => true,
            (ModbusValueType.UInt16, SignalValueType.UInt16) => true,
            (ModbusValueType.Int, SignalValueType.Int16 or SignalValueType.Int32) => true,
            (ModbusValueType.Real, SignalValueType.Float32) => true,
            (ModbusValueType.String, SignalValueType.String) => true,
            _ => false
        };
    }
}
