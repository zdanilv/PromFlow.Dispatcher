using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Microsoft.Extensions.Logging;

namespace Configurator.Infrastructure.Modbus.Runtime;

internal sealed class ModbusBitWriter(
    IModbusRuntimeService runtime,
    IModbusClientService client,
    IModbusServerService server,
    ILogger<ModbusBitWriter> logger,
    TimeSpan? edgeTimeout = null) : IModbusBitWriter
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _edgeTimeout = edgeTimeout ?? TimeSpan.FromSeconds(2);

    public async Task<ModbusOperationResult> PulseAsync(
        ModbusBitAddressOptions address,
        int pulseDurationMs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        // Callers may edit their configuration while the pulse is queued or held.
        var capturedAddress = address.Clone();
        var validation = Validate(capturedAddress, pulseDurationMs);
        if (!validation.Succeeded)
            return validation;

        try
        {
            await _gate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Cancelled("Acknowledgement was cancelled before activation; no edge was sent.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = runtime.Status;
            var provider = status.ClientState == ModbusConnectionState.Running
                ? client as IModbusBitPulseProvider
                : status.ServerState == ModbusConnectionState.Running
                    ? server as IModbusBitPulseProvider
                    : null;
            if (provider is null)
            {
                return ModbusOperationResult.Failure(
                    "ModbusNotRunning",
                    "A running Modbus role with a safe acknowledgement transport is required.");
            }

            IModbusBitPulseTarget target;
            try
            {
                target = provider.CaptureBitPulseTarget(capturedAddress);
            }
            catch (Exception ex)
            {
                return ModbusOperationResult.Failure(
                    "ModbusBitPulseRisingEdgeFailed",
                    "Acknowledgement target is unavailable; no edge was sent.", ex.Message);
            }

            Exception? risingFailure = null;
            var cancelled = false;
            try
            {
                await WriteEdgeAsync(target, true, cancellationToken);
            }
            catch (ModbusBitPulseNotSentException ex)
            {
                return ModbusOperationResult.Failure(
                    cancellationToken.IsCancellationRequested
                        ? "ModbusBitPulseCancelled" : "ModbusBitPulseRisingEdgeFailed",
                    "Acknowledgement activation failed before the write; no edge was sent.",
                    $"Target: {target.Description}. Rising edge: {ex.Message}");
            }
            catch (Exception ex)
            {
                // A lost reply does not prove that the PLC did not apply true.
                // Never retry activation; only attempt release on the captured target.
                risingFailure = ex;
                cancelled = cancellationToken.IsCancellationRequested;
            }

            if (risingFailure is null)
            {
                try
                {
                    await Task.Delay(pulseDurationMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
            }

            try
            {
                // Release has its own deadline, independent of the caller's cancellation.
                await WriteEdgeAsync(target, false, CancellationToken.None);
            }
            catch (Exception ex)
            {
                var details = $"Target: {target.Description}. Rising edge: "
                    + (risingFailure is null ? "confirmed" : risingFailure.Message)
                    + $". Falling edge: {ex.Message}. ACK bit physical state is unknown.";
                logger.LogWarning(ex, "Failed to release acknowledgement: {Details}", details);
                return ModbusOperationResult.Failure(
                    "ModbusBitPulseReleaseFailed",
                    "Acknowledgement release failed; ACK bit physical state is unknown.", details);
            }

            if (risingFailure is not null)
            {
                return ModbusOperationResult.Failure(
                    cancelled ? "ModbusBitPulseCancelled" : "ModbusBitPulseRisingEdgeFailed",
                    "Acknowledgement activation was not confirmed; release was confirmed.",
                    $"Target: {target.Description}. Rising edge: {risingFailure.Message}. Falling edge: confirmed.");
            }

            return cancelled
                ? Cancelled("Acknowledgement was cancelled during the hold; release was confirmed.")
                : ModbusOperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            return Cancelled("Acknowledgement was cancelled before activation; no edge was sent.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task WriteEdgeAsync(
        IModbusBitPulseTarget target, bool value, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_edgeTimeout);
        if (cancellationToken.IsCancellationRequested)
            throw new ModbusBitPulseNotSentException("The ACK edge was cancelled before submission.");
        try
        {
            await target.WriteAsync(value, deadline.Token).WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Acknowledgement {(value ? "activation" : "release")} timed out.", ex);
        }
    }

    private static ModbusOperationResult Cancelled(string message)
        => ModbusOperationResult.Failure("ModbusBitPulseCancelled", message);

    private static ModbusOperationResult Validate(ModbusBitAddressOptions address, int pulseDurationMs)
    {
        if (pulseDurationMs is < 1 or > 60000)
            return ModbusOperationResult.Failure("ModbusBitPulseDurationInvalid",
                "Modbus bit pulse duration must be in range 1..60000 ms.");
        if (address.Address is < 0 or > ushort.MaxValue)
            return ModbusOperationResult.Failure("ModbusBitAddressInvalid",
                "Modbus bit offset must be in range 0..65535.");
        return address.Area switch
        {
            ModbusDataArea.Coil when address.BitIndex is not null => ModbusOperationResult.Failure(
                "ModbusBitCoilBitIndexInvalid", "Coil bit write must not define BitIndex."),
            ModbusDataArea.HoldingRegister when address.BitIndex is < 0 or > 15 or null => ModbusOperationResult.Failure(
                "ModbusBitRegisterBitInvalid", "Holding Register bit write must define BitIndex in range 0..15."),
            ModbusDataArea.Coil or ModbusDataArea.HoldingRegister => ModbusOperationResult.Success(),
            _ => ModbusOperationResult.Failure("ModbusBitAreaUnsupported",
                $"Modbus bit area '{address.Area}' is unsupported.")
        };
    }
}
