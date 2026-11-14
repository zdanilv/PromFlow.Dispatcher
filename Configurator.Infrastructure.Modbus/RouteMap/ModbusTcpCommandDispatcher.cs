using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Application.Services.Signals;
using Configurator.Infrastructure.Modbus.Runtime;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Configurator.Infrastructure.Modbus.RouteMap;

public sealed class ModbusTcpCommandDispatcher(
    IModbusTcpService modbusService,
    IOptionsMonitor<ModbusOptions> optionsMonitor) : IEquipmentCommandDispatcher
{
    private static readonly ModbusDataMapValidator DataMapValidator = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _pulseGates = new(StringComparer.OrdinalIgnoreCase);

    public async Task DispatchAsync(
        SignalWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        var options = optionsMonitor.CurrentValue;
        var point = options.DataMap.FirstOrDefault(
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
            if (point.Type != ModbusValueType.Bool)
                throw new InvalidOperationException($"Modbus pulse signal '{point.Name}' requires Bool type.");

            if (request.RequiredWriteMode == ModbusWriteMode.Pulse && point.Access != ModbusDataAccess.ReadWrite)
                throw new InvalidOperationException($"Signal '{point.Name}' requires ReadWrite access.");

            if (request.Value is not bool pulseRequested || !pulseRequested)
            {
                return;
            }

            ThrowIfFailed(DataMapValidator.Validate(options, ModbusRunMode.None));

            var gate = _pulseGates.GetOrAdd(point.Name, _ => new SemaphoreSlim(1, 1));
            if (!await gate.WaitAsync(0, cancellationToken))
                throw new InvalidOperationException($"A pulse for Modbus signal '{point.Name}' is already in progress.");

            var releaseGate = true;
            try
            {
                var facade = modbusService as ModbusTcpService;
                var target = facade?.CapturePulseTarget();
                Exception? pulseError = null;
                try
                {
                    // Even a failed transport response may follow a successful physical write.
                    await WritePulseEdgeAsync(point, target, true, cancellationToken);
                    await Task.Delay(point.PulseDurationMs, cancellationToken);
                }
                catch (PulseMappingException)
                {
                    // The facade rejected the mapping before calling the transport.
                    throw;
                }
                catch (Exception ex)
                {
                    pulseError = ex;
                }

                Exception? resetError = null;
                using var resetTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var resetTask = WritePulseEdgeAsync(point, target, false, resetTimeout.Token);
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
        => ThrowIfFailed(await modbusService.SetAsync(signalId, value, cancellationToken));

    private async Task WritePulseEdgeAsync(ModbusDataPointOptions point, ModbusTcpService.PulseTarget? target,
        bool value, CancellationToken cancellationToken)
        => ThrowIfFailed(modbusService is ModbusTcpService facade && target is { } pulseTarget
            ? await facade.SetPulseEdgeAsync(point, pulseTarget, value, cancellationToken)
            : await modbusService.SetAsync(point.Name, value, cancellationToken));

    private static void ThrowIfFailed(ModbusOperationResult result)
    {
        if (result.ErrorCode == ModbusTcpService.PulseMappingRejected)
            throw new PulseMappingException(result.ErrorMessage);
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

    private sealed class PulseMappingException(string? message) : InvalidOperationException(message);
}
