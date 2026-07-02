using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Signals;
using Microsoft.Extensions.Options;

namespace Configurator.Infrastructure.Modbus.RouteMap;

public sealed class ModbusTcpCommandDispatcher(
    IModbusTcpService modbusService,
    IOptionsMonitor<ModbusOptions> optionsMonitor) : IEquipmentCommandDispatcher
{
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

        if (point.WriteMode == ModbusWriteMode.Pulse)
        {
            if (request.Value is not bool pulseRequested || !pulseRequested)
            {
                return;
            }

            await WriteOrThrowAsync(point.Name, true, cancellationToken);
            try
            {
                await Task.Delay(point.PulseDurationMs, cancellationToken);
            }
            finally
            {
                using var resetTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await WriteOrThrowAsync(point.Name, false, resetTimeout.Token);
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
