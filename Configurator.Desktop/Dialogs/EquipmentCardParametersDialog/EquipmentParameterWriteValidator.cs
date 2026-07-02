using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Signals;
using Microsoft.Extensions.Options;
using System.Text;

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

        if (request.ValueType == SignalValueType.String && point.Type == ModbusValueType.String)
        {
            var text = request.Value?.ToString() ?? string.Empty;
            var byteCount = Encoding.UTF8.GetByteCount(text);
            var capacity = Math.Max(0, point.Length) * 2;
            if (byteCount > capacity)
            {
                return $"SignalId {request.SignalId}: строка занимает {byteCount} байт, доступно {capacity}. Увеличьте Length во вкладке SignalId ↔ Modbus.";
            }
        }

        return null;
    }

    private static bool IsCompatible(ModbusValueType modbusType, SignalValueType signalType) =>
        SignalModbusTypeCompatibility.IsCompatible(signalType, modbusType);
}
