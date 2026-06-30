using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Signals;
using Microsoft.Extensions.Options;

namespace Configurator.Desktop.Dialogs.EquipmentCardParametersDialog;

public sealed class EquipmentParameterWriteValidator(
    IRouteMapSignalRuntime routeMapSignalRuntime,
    IOptionsMonitor<ModbusOptions> optionsMonitor) : IEquipmentParameterWriteValidator
{
    public string? Validate(SignalWriteRequest request)
    {
        if (routeMapSignalRuntime.CurrentSource == RouteMapSignalSource.Mock)
        {
            return null;
        }

        var point = optionsMonitor.CurrentValue.DataMap.FirstOrDefault(
            candidate => string.Equals(candidate.Name, request.SignalId, StringComparison.OrdinalIgnoreCase));
        if (point is null)
        {
            return $"SignalId {request.SignalId} не настроен во вкладке SignalId ↔ Modbus.";
        }

        if (!point.IsWritable)
        {
            return $"SignalId {request.SignalId} настроен только для чтения во вкладке SignalId ↔ Modbus.";
        }

        if (!IsCompatible(point.Type, request.ValueType))
        {
            return $"SignalId {request.SignalId}: тип Modbus {point.Type} не совместим с типом параметра {request.ValueType}.";
        }

        return null;
    }

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
