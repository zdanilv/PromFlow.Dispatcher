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
    ILogger<ModbusBitWriter> logger) : IModbusBitWriter
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<ModbusOperationResult> PulseAsync(
        ModbusBitAddressOptions address,
        int pulseDurationMs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (pulseDurationMs is < 1 or > 60000)
        {
            return ModbusOperationResult.Failure(
                "ModbusBitPulseDurationInvalid",
                "Modbus bit pulse duration must be in range 1..60000 ms.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return address.Area switch
            {
                ModbusDataArea.Coil => await PulseCoilAsync(address, pulseDurationMs, cancellationToken),
                ModbusDataArea.HoldingRegister => await PulseRegisterBitAsync(address, pulseDurationMs, cancellationToken),
                _ => ModbusOperationResult.Failure(
                    "ModbusBitAreaUnsupported",
                    $"Modbus bit area '{address.Area}' is unsupported.")
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ModbusOperationResult> PulseCoilAsync(
        ModbusBitAddressOptions address,
        int pulseDurationMs,
        CancellationToken cancellationToken)
    {
        if (address.BitIndex is not null)
        {
            return ModbusOperationResult.Failure(
                "ModbusBitCoilBitIndexInvalid",
                "Coil bit write must not define BitIndex.");
        }

        if (!TryResolveTarget(out var target, out var failure))
        {
            return failure;
        }

        var set = await WriteCoilAsync(target, address.Address, true, cancellationToken);
        if (!set.Succeeded)
        {
            return set;
        }

        try
        {
            await Task.Delay(pulseDurationMs, cancellationToken);
        }
        finally
        {
            using var resetTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await WriteCoilAsync(target, address.Address, false, resetTimeout.Token);
        }

        return ModbusOperationResult.Success();
    }

    private async Task<ModbusOperationResult> PulseRegisterBitAsync(
        ModbusBitAddressOptions address,
        int pulseDurationMs,
        CancellationToken cancellationToken)
    {
        if (address.BitIndex is < 0 or > 15 or null)
        {
            return ModbusOperationResult.Failure(
                "ModbusBitRegisterBitInvalid",
                "Holding Register bit write must define BitIndex in range 0..15.");
        }

        if (!TryResolveTarget(out var target, out var targetFailure))
        {
            return targetFailure;
        }

        if (!TryReadRegisterWord(target, address.Address, out var initialWord))
        {
            return ModbusOperationResult.Failure(
                "ModbusBitRegisterSnapshotUnavailable",
                $"Holding register {address.Address} has no snapshot for bit pulse.");
        }

        var mask = checked((ushort)(1 << address.BitIndex.Value));
        var trueWord = (ushort)(initialWord | mask);
        var set = await WriteRegisterAsync(target, address.Address, trueWord, cancellationToken);
        if (!set.Succeeded)
        {
            return set;
        }

        try
        {
            await Task.Delay(pulseDurationMs, cancellationToken);
        }
        finally
        {
            using var resetTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var baseWord = TryReadRegisterWord(target, address.Address, out var latestWord)
                ? latestWord
                : trueWord;
            var falseWord = (ushort)(baseWord & ~mask);
            await WriteRegisterAsync(target, address.Address, falseWord, resetTimeout.Token);
        }

        return ModbusOperationResult.Success();
    }

    private bool TryResolveTarget(
        out ModbusBitWriteTarget target,
        out ModbusOperationResult failure)
    {
        var status = runtime.Status;
        if (status.ClientState == ModbusConnectionState.Running)
        {
            target = ModbusBitWriteTarget.Client;
            failure = ModbusOperationResult.Success();
            return true;
        }

        if (status.ServerState == ModbusConnectionState.Running)
        {
            target = ModbusBitWriteTarget.Server;
            failure = ModbusOperationResult.Success();
            return true;
        }

        target = ModbusBitWriteTarget.Client;
        failure = ModbusOperationResult.Failure(
            "ModbusNotRunning",
            "Modbus client or server must be running before writing acknowledgement.");
        return false;
    }

    private bool TryReadRegisterWord(
        ModbusBitWriteTarget target,
        int address,
        out ushort word)
    {
        var preferred = target == ModbusBitWriteTarget.Client
            ? runtime.ClientSnapshot
            : runtime.ServerSnapshot;
        var fallback = target == ModbusBitWriteTarget.Client
            ? runtime.ServerSnapshot
            : runtime.ClientSnapshot;

        if (TryReadRegisterWord(preferred, address, out word))
        {
            return true;
        }

        return TryReadRegisterWord(fallback, address, out word);
    }

    private static bool TryReadRegisterWord(
        ModbusSnapshot snapshot,
        int address,
        out ushort word)
    {
        if (address >= 0 && address < snapshot.HoldingRegisters.Count)
        {
            word = snapshot.HoldingRegisters[address];
            return true;
        }

        word = 0;
        return false;
    }

    private async Task<ModbusOperationResult> WriteCoilAsync(
        ModbusBitWriteTarget target,
        int address,
        bool value,
        CancellationToken cancellationToken)
    {
        try
        {
            if (target == ModbusBitWriteTarget.Client)
            {
                await client.WriteCoilAsync(address, value, cancellationToken);
            }
            else
            {
                await server.SetCoilAsync(address, value, cancellationToken);
            }

            return ModbusOperationResult.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to write acknowledgement Coil {Address}", address);
            return ModbusOperationResult.Failure(
                "ModbusBitWriteFailed",
                $"Failed to write acknowledgement Coil {address}.",
                ex.Message);
        }
    }

    private async Task<ModbusOperationResult> WriteRegisterAsync(
        ModbusBitWriteTarget target,
        int address,
        ushort value,
        CancellationToken cancellationToken)
    {
        try
        {
            if (target == ModbusBitWriteTarget.Client)
            {
                await client.WriteRegisterAsync(address, value, cancellationToken);
            }
            else
            {
                await server.SetRegisterAsync(address, value, cancellationToken);
            }

            return ModbusOperationResult.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to write acknowledgement Holding Register {Address}", address);
            return ModbusOperationResult.Failure(
                "ModbusBitWriteFailed",
                $"Failed to write acknowledgement Holding Register {address}.",
                ex.Message);
        }
    }

    private enum ModbusBitWriteTarget
    {
        Client,
        Server
    }
}
