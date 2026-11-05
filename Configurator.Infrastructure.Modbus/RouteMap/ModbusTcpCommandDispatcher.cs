using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Signals;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Configurator.Infrastructure.Modbus.RouteMap;

public sealed class ModbusTcpCommandDispatcher(
    IModbusTcpService modbusService,
    IOptionsMonitor<ModbusOptions> optionsMonitor) : IEquipmentCommandDispatcher
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _pulseGates = new(StringComparer.OrdinalIgnoreCase);

    public async Task DispatchAsync(
        SignalWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        var point = optionsMonitor.CurrentValue.DataMap.FirstOrDefault(
            candidate => string.Equals(candidate.Name, request.SignalId, StringComparison.OrdinalIgnoreCase));
        if (point is null)
        {
            throw new InvalidOperationException($"Modbus signal '{request.SignalId}' is not configured.");
        }

        if (!point.IsWritable)
        {
            throw new InvalidOperationException($"Modbus signal '{request.SignalId}' is read-only.");
        }

        if (!IsCompatible(point.Type, request.ValueType))
        {
            throw new InvalidOperationException(
                $"Signal '{request.SignalId}' expects {point.Type}, but RouteMap requested {request.ValueType}.");
        }

        if (request.RequiredWriteMode is { } requiredWriteMode && point.WriteMode != requiredWriteMode)
        {
            throw new InvalidOperationException(
                $"Signal '{request.SignalId}' requires {requiredWriteMode} write mode, but Modbus.DataMap uses {point.WriteMode}.");
        }

        if (point.WriteMode == ModbusWriteMode.Pulse)
        {
            if (request.Value is not bool pulseRequested || !pulseRequested)
            {
                return;
            }

            var gate = _pulseGates.GetOrAdd(point.Name, _ => new SemaphoreSlim(1, 1));
            if (!await gate.WaitAsync(0, cancellationToken))
                throw new InvalidOperationException($"A pulse for Modbus signal '{point.Name}' is already in progress.");

            var releaseGate = true;
            try
            {
                Exception? pulseError = null;
                try
                {
                    // Even a failed transport response may follow a successful physical write.
                    await WriteOrThrowAsync(point.Name, true, cancellationToken);
                    await Task.Delay(point.PulseDurationMs, cancellationToken);
                }
                catch (Exception ex)
                {
                    pulseError = ex;
                }

                Exception? resetError = null;
                using var resetTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var resetTask = WriteOrThrowAsync(point.Name, false, resetTimeout.Token);
                try
                {
                    await resetTask.WaitAsync(resetTimeout.Token);
                }
                catch (Exception ex)
                {
                    resetError = ex;
                    if (!resetTask.IsCompleted)
                    {
                        // Keep this signal inhibited until a non-cooperative reset finishes:
                        // a late false must never cut short a subsequent pulse.
                        releaseGate = false;
                        _ = resetTask.ContinueWith(task =>
                        {
                            _ = task.Exception;
                            gate.Release();
                        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    }
                    else
                    {
                        _ = resetTask.Exception;
                    }
                }

                if (pulseError is not null && resetError is not null)
                    throw new AggregateException($"Pulse for Modbus signal '{point.Name}' failed and its reset also failed.", pulseError, resetError);
                if (pulseError is not null)
                    ExceptionDispatchInfo.Capture(pulseError).Throw();
                if (resetError is not null)
                    throw new InvalidOperationException($"Reset of pulse for Modbus signal '{point.Name}' failed; the PLC command state is unknown.", resetError);
            }
            finally
            {
                if (releaseGate)
                    gate.Release();
            }

            return;
        }

        await WriteOrThrowAsync(point.Name, request.Value, cancellationToken);
    }

    private async Task WriteOrThrowAsync<T>(string signalId, T value, CancellationToken cancellationToken)
    {
        var result = await modbusService.SetAsync(signalId, value, cancellationToken);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                result.ErrorDetails is null
                    ? result.ErrorMessage
                    : $"{result.ErrorMessage} {result.ErrorDetails}");
        }
    }

    private static bool IsCompatible(ModbusValueType modbusType, SignalValueType signalType) =>
        SignalModbusTypeCompatibility.IsCompatible(signalType, modbusType);
}
