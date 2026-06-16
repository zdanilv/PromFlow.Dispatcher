using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Configurator.Desktop.Workspace.RouteMap.Services;

public sealed class RouteMapModbusBindingDiagnostics : IDisposable
{
    private readonly RouteMapConfigurationManager _configurationManager;
    private readonly IOptionsMonitor<ModbusOptions> _optionsMonitor;
    private readonly ILogger<RouteMapModbusBindingDiagnostics> _logger;
    private readonly IRouteMapSignalRuntime _signalRuntime;
    private readonly IDisposable _definitionSubscription;
    private readonly IDisposable? _optionsSubscription;

    public RouteMapModbusBindingDiagnostics(
        RouteMapConfigurationManager configurationManager,
        IOptionsMonitor<ModbusOptions> optionsMonitor,
        IRouteMapSignalRuntime signalRuntime,
        ILogger<RouteMapModbusBindingDiagnostics> logger)
    {
        _configurationManager = configurationManager;
        _optionsMonitor = optionsMonitor;
        _signalRuntime = signalRuntime;
        _logger = logger;
        _definitionSubscription = configurationManager.DefinitionChanges.Subscribe(Validate);
        _optionsSubscription = optionsMonitor.OnChange((_, _) => Validate(configurationManager.CurrentDefinition));
        _signalRuntime.SourceChanged += OnSourceChanged;
    }

    public void Dispose()
    {
        _definitionSubscription.Dispose();
        _optionsSubscription?.Dispose();
        _signalRuntime.SourceChanged -= OnSourceChanged;
    }

    private void OnSourceChanged(object? sender, RouteMapSignalSourceChangedEventArgs eventArgs) =>
        Validate(_configurationManager.CurrentDefinition);

    private void Validate(RouteMapDefinition definition)
    {
        if (_signalRuntime.CurrentSource != RouteMapSignalSource.Modbus)
        {
            return;
        }

        var points = _optionsMonitor.CurrentValue.DataMap
            .Where(point => !string.IsNullOrWhiteSpace(point.Name))
            .GroupBy(point => point.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var binding in EnumerateBindings(definition))
        {
            if (string.Equals(binding.SignalId, "connection.status", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!points.TryGetValue(binding.SignalId, out var point))
            {
                _logger.LogWarning("RouteMap signal {SignalId} is missing from Modbus.DataMap", binding.SignalId);
                continue;
            }

            if (binding.Direction is SignalBindingDirection.Read or SignalBindingDirection.ReadWrite
                && !point.IsReadable)
            {
                _logger.LogWarning("RouteMap signal {SignalId} requires read access", binding.SignalId);
            }

            if (binding.Direction is SignalBindingDirection.Write or SignalBindingDirection.ReadWrite
                && !point.IsWritable)
            {
                _logger.LogWarning("RouteMap signal {SignalId} requires write access", binding.SignalId);
            }

            if (!TypesMatch(binding.ValueType, point.Type))
            {
                _logger.LogWarning(
                    "RouteMap signal {SignalId} expects {SignalType}, but Modbus.DataMap defines {ModbusType}",
                    binding.SignalId,
                    binding.ValueType,
                    point.Type);
            }
        }
    }

    private static IEnumerable<SignalBinding> EnumerateBindings(RouteMapDefinition definition)
    {
        var bindings = definition.Nodes.SelectMany(node => node.Bindings)
            .Concat(definition.Segments.SelectMany(segment => segment.Bindings))
            .Concat(definition.Vehicles.SelectMany(vehicle => vehicle.Bindings))
            .Concat(definition.MapEquipment.SelectMany(card => card.Bindings));

        if (definition.TopBar is not null)
        {
            bindings = bindings.Concat(
            [
                definition.TopBar.Automatic.Binding,
                definition.TopBar.Manual.Binding,
                definition.TopBar.Emergency.Binding
            ]);
        }

        return bindings
            .Where(binding => !string.IsNullOrWhiteSpace(binding.SignalId))
            .GroupBy(binding => binding.SignalId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());
    }

    private static bool TypesMatch(SignalValueType signalType, ModbusValueType modbusType)
    {
        return (signalType, modbusType) switch
        {
            (SignalValueType.Bool, ModbusValueType.Bool) => true,
            (SignalValueType.UInt16, ModbusValueType.UInt16) => true,
            (SignalValueType.Int16 or SignalValueType.Int32, ModbusValueType.Int) => true,
            (SignalValueType.Float32, ModbusValueType.Real) => true,
            (SignalValueType.String, ModbusValueType.String) => true,
            _ => false
        };
    }
}
