using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.SignalMapping;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Configurator.Desktop.Workspace.RouteMap.Services;

public sealed class RouteMapModbusBindingDiagnostics : IDisposable
{
    private static readonly ModbusDataMapValidator DataMapValidator = new();
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

        var options = _optionsMonitor.CurrentValue;
        var points = options.DataMap
            .Where(point => !string.IsNullOrWhiteSpace(point.Name))
            .GroupBy(point => point.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var inventory = RouteMapSignalInventory.Build(definition);
        foreach (var item in inventory)
        {
            if (item.IsSystem)
            {
                continue;
            }

            if (!points.TryGetValue(item.SignalId, out var point))
            {
                _logger.LogWarning("RouteMap signal {SignalId} is missing from Modbus.DataMap", item.SignalId);
                continue;
            }

            if (item.RequiredAccess is ModbusDataAccess.Read or ModbusDataAccess.ReadWrite
                && !point.IsReadable)
            {
                _logger.LogWarning("RouteMap signal {SignalId} requires read access", item.SignalId);
            }

            if (item.RequiredAccess is ModbusDataAccess.Write or ModbusDataAccess.ReadWrite
                && !point.IsWritable)
            {
                _logger.LogWarning("RouteMap signal {SignalId} requires write access", item.SignalId);
            }

            if (!SignalModbusTypeCompatibility.IsCompatible(item.ExpectedType, point.Type))
            {
                _logger.LogWarning(
                    "RouteMap signal {SignalId} expects {SignalType}, but Modbus.DataMap defines {ModbusType}",
                    item.SignalId,
                    item.ExpectedType,
                    point.Type);
            }
            if (item.RequiresPulseWriteMode && point.WriteMode != ModbusWriteMode.Pulse)
            {
                _logger.LogWarning("RouteMap signal {SignalId} requires Pulse write mode, but Modbus.DataMap uses {WriteMode}",
                    item.SignalId, point.WriteMode);
            }
        }

        var endpoints = _optionsMonitor.Get(ModbusOptions.DemoSectionName);
        var validationOptions = new ModbusOptions
        {
            Client = endpoints.Client,
            Server = endpoints.Server,
            DataMap = options.DataMap,
        };
        var validation = DataMapValidator.Validate(validationOptions, ModbusRunMode.None);
        if (validation.Succeeded && endpoints.Client.Enabled)
            validation = DataMapValidator.Validate(validationOptions, ModbusRunMode.Client);
        if (validation.Succeeded && endpoints.Server.Enabled)
            validation = DataMapValidator.Validate(validationOptions, ModbusRunMode.Server);
        if (!validation.Succeeded)
            _logger.LogWarning("Invalid Modbus.DataMap: {ErrorMessage}", validation.ErrorMessage);
    }
}
