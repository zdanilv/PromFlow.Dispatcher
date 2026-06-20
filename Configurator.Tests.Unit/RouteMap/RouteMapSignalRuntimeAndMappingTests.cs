using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Avalonia.Media;
using Configurator.Application.Services;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Configurator.Desktop.Workspace.RouteMap.Settings;
using Configurator.Desktop.Workspace.RouteMap.SignalMapping;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class RouteMapSignalRuntimeAndMappingTests
{
    [Fact]
    public async Task SignalRuntime_SwitchesObservableAndCommandBackendWithoutReplacingSubscribers()
    {
        var mockProvider = new ManualSignalProvider();
        var modbusProvider = new ManualSignalProvider();
        var mockCommands = new RecordingCommandDispatcher();
        var modbusCommands = new RecordingCommandDispatcher();
        using var runtime = new RouteMapSignalRuntime(
            mockProvider,
            mockCommands,
            modbusProvider,
            modbusCommands,
            RouteMapSignalSource.Mock);
        var received = new List<string>();
        using var subscription = runtime.Observe().Subscribe(snapshot => received.Add(
            Convert.ToString(snapshot["source"].Value) ?? string.Empty));

        mockProvider.Publish("mock-1");
        runtime.SwitchSource(RouteMapSignalSource.Modbus);
        mockProvider.Publish("mock-ignored");
        modbusProvider.Publish("modbus-1");
        await runtime.DispatchAsync(new SignalWriteRequest("command", true, SignalValueType.Bool));

        Assert.Equal(["mock-1", "modbus-1"], received);
        Assert.Empty(mockCommands.Requests);
        Assert.Single(modbusCommands.Requests);
        Assert.Equal(RouteMapSignalSource.Modbus, runtime.CurrentSource);
    }

    [Fact]
    public async Task RouteMapSettings_ApplyIsSessionOnlyAndSavePersistsSelectedSource()
    {
        using var scope = new ConfigurationScope();
        var mockProvider = new ManualSignalProvider();
        var modbusProvider = new ManualSignalProvider();
        using var runtime = new RouteMapSignalRuntime(
            mockProvider,
            new RecordingCommandDispatcher(),
            modbusProvider,
            new RecordingCommandDispatcher(),
            RouteMapSignalSource.Mock);
        var config = new RecordingAppConfigService();
        using var viewModel = new RouteMapSettingsViewModel(
            scope.Manager,
            scope.Storage,
            new NullFilePicker(),
            runtime,
            config)
        {
            UseMockSimulation = false
        };

        viewModel.Apply();

        Assert.Equal(RouteMapSignalSource.Modbus, runtime.CurrentSource);
        Assert.Null(config.SavedRouteMapRuntime);

        await viewModel.SaveAsync();

        Assert.Equal(RouteMapSignalSource.Modbus, config.SavedRouteMapRuntime?.SignalSource);
    }

    [Fact]
    public void SignalInventory_GroupsBindingsAndReportsTypeConflictsAndSystemSignal()
    {
        var definition = RouteMapSeed.Create();
        var firstNode = definition.Nodes[0];
        var changedNode = firstNode with
        {
            Bindings = firstNode.Bindings.Concat(
            [
                new SignalBinding(
                    SignalBindingRole.Text,
                    "system.emergency",
                    SignalBindingDirection.Read,
                    SignalValueType.String)
            ]).ToArray()
        };
        definition = definition with
        {
            Nodes = definition.Nodes.Select(node => node.Id == firstNode.Id ? changedNode : node).ToArray()
        };

        var inventory = RouteMapSignalInventory.Build(definition);

        Assert.True(inventory.Single(item => item.SignalId == "system.emergency").HasTypeConflict);
        Assert.Equal(
            RouteMapSignalElementCategory.Common,
            inventory.Single(item => item.SignalId == "system.emergency").Category);
        Assert.True(inventory.Single(item => item.SignalId == "connection.status").IsSystem);
        Assert.Equal(
            RouteMapSignalElementCategory.System,
            inventory.Single(item => item.SignalId == "connection.status").Category);
        Assert.Equal(inventory.Count, inventory.Select(item => item.SignalId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void SignalInventory_AssignsEveryElementCategory()
    {
        var definition = RouteMapSeed.Create();
        var binding = new SignalBinding(
            SignalBindingRole.State,
            "test.vehicle.state",
            SignalBindingDirection.Read,
            SignalValueType.String);
        definition = definition with
        {
            Vehicles = definition.Vehicles.Concat(
            [
                new RouteVehicle(
                    "test-vehicle",
                    "Test vehicle",
                    0,
                    0,
                    RouteObjectState.Unknown,
                    [binding])
            ]).ToArray()
        };

        var inventory = RouteMapSignalInventory.Build(definition);

        Assert.Contains(inventory, item => item.Category == RouteMapSignalElementCategory.System);
        Assert.Contains(inventory, item => item.Category == RouteMapSignalElementCategory.TopBar);
        Assert.Contains(inventory, item => item.Category == RouteMapSignalElementCategory.Node);
        Assert.Contains(inventory, item => item.Category == RouteMapSignalElementCategory.Segment);
        Assert.Contains(inventory, item => item.Category == RouteMapSignalElementCategory.Card);
        Assert.Equal(
            RouteMapSignalElementCategory.Vehicle,
            inventory.Single(item => item.SignalId == binding.SignalId).Category);
    }

    [Fact]
    public void MappingGroups_HaveFixedOrderAndPreserveDraftAndExpansionOnRebuild()
    {
        using var scope = new ConfigurationScope();
        var options = new ModbusOptions
        {
            Client = new ModbusEndpointOptions { CoilCount = 20, RegisterCount = 20 },
            Server = new ModbusEndpointOptions { CoilCount = 20, RegisterCount = 20 }
        };
        using var viewModel = new RouteMapSignalMappingViewModel(
            scope.Manager,
            new TestOptionsMonitor(options),
            new RecordingAppConfigService(),
            new ModbusDataMapValidator(),
            new RecordingDataMapRuntime());
        var group = viewModel.Groups.First(item => item.Category == RouteMapSignalElementCategory.Node);
        var row = group.Rows.First();
        group.IsExpanded = false;
        row.Address = 7;

        viewModel.RebuildRows(scope.Manager.CurrentDefinition, options.Clone(), preserveDraft: true);

        var expectedOrder = RouteMapSignalMappingGroup.DisplayOrder
            .Where(category => viewModel.Rows.Any(row => row.Category == category));
        Assert.Equal(expectedOrder, viewModel.Groups.Select(item => item.Category));
        Assert.All(viewModel.Groups, item => Assert.NotEmpty(item.Rows));
        Assert.False(viewModel.Groups.Single(item => item.Category == RouteMapSignalElementCategory.Node).IsExpanded);
        Assert.Equal(7, viewModel.Rows.Single(item => item.SignalId == row.SignalId).Address);
        Assert.Equal(
            viewModel.Rows.Count,
            viewModel.Groups.SelectMany(item => item.Rows).Select(item => item.SignalId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void MappingDefaults_UsePulseForMomentaryCommandsAndDemoAddressBases()
    {
        using var scope = new ConfigurationScope();
        var draft = scope.Manager.CreateDraft();
        draft.Cards.Single().StartButtonKind = RouteCommandButtonKind.Momentary;
        draft.Cards.Single().StartOffFeedbackEnabled = false;
        draft.Cards.Single().Bindings.Remove(draft.Cards.Single().Bindings.Single(x => x.Role == SignalBindingRole.StartOffFeedback));
        Assert.True(scope.Manager.Apply(draft).IsSuccess);
        var routeOptions = new ModbusOptions();
        var demoOptions = new ModbusOptions
        {
            Client = new ModbusEndpointOptions
            {
                CoilStartAddress = 100,
                HoldingRegisterStartAddress = 200,
                CoilCount = 20,
                RegisterCount = 20
            },
            Server = new ModbusEndpointOptions
            {
                CoilStartAddress = 300,
                HoldingRegisterStartAddress = 400,
                CoilCount = 20,
                RegisterCount = 20
            }
        };
        using var viewModel = new RouteMapSignalMappingViewModel(
            scope.Manager,
            new TestOptionsMonitor(routeOptions, demoOptions),
            new RecordingAppConfigService(),
            new ModbusDataMapValidator(),
            new RecordingDataMapRuntime());
        var row = viewModel.Rows.Single(item => item.SignalId == "equip.bucket.start");

        viewModel.CreateMappingCommand.Execute(row).Subscribe();

        Assert.Equal(ModbusWriteMode.Pulse, row.WriteMode);
        Assert.Equal("100", row.ClientPhysicalAddress);
        Assert.Equal("300", row.ServerPhysicalAddress);
    }

    [Fact]
    public void SignalInventory_ExposesOffFeedbackSignalsAsReadOnly()
    {
        var definition = RouteMapSeed.Create();

        var inventory = RouteMapSignalInventory.Build(definition);

        var cardOff = inventory.Single(item => item.SignalId == "equip.bucket.start.off");
        Assert.Equal(ModbusDataAccess.Read, cardOff.RequiredAccess);
        Assert.Equal(SignalValueType.Bool, cardOff.ExpectedType);

        var topBarOff = inventory.Single(item => item.SignalId == "system.emergency.off");
        Assert.Equal(ModbusDataAccess.Read, topBarOff.RequiredAccess);
        Assert.Equal(RouteMapSignalElementCategory.TopBar, topBarOff.Category);
        Assert.DoesNotContain(inventory, item => item.SignalId == "system.mode.manual.off");
        Assert.DoesNotContain(inventory, item => item.SignalId == "route.node.bsu_1.loader.off");
    }

    [Fact]
    public void MappingRow_UsesGrayBackgroundOnlyWhenUnused()
    {
        using var scope = new ConfigurationScope();
        using var viewModel = new RouteMapSignalMappingViewModel(
            scope.Manager,
            new TestOptionsMonitor(new ModbusOptions()),
            new RecordingAppConfigService(),
            new ModbusDataMapValidator(),
            new RecordingDataMapRuntime());
        var row = viewModel.Rows.First(item => !item.IsSystem && !item.IsMapped);

        var unusedBrush = Assert.IsType<SolidColorBrush>(row.RowBackground);
        Assert.Equal(Color.Parse("#F1F3F5"), unusedBrush.Color);

        row.IsMapped = true;

        Assert.Same(Brushes.White, row.RowBackground);
    }

    [Fact]
    public async Task MappingSave_ReplacesRoutePointAndPreservesUnrelatedDataMapPoints()
    {
        using var scope = new ConfigurationScope();
        var options = new ModbusOptions
        {
            Client = new ModbusEndpointOptions { CoilCount = 20, RegisterCount = 20 },
            Server = new ModbusEndpointOptions { CoilCount = 20, RegisterCount = 20 },
            DataMap =
            [
                new ModbusDataPointOptions
                {
                    Name = "diagnostic.unrelated",
                    Area = ModbusDataArea.Coil,
                    Address = 19,
                    Length = 1,
                    Type = ModbusValueType.Bool,
                    Access = ModbusDataAccess.Read
                }
            ]
        };
        var monitor = new TestOptionsMonitor(options);
        var config = new RecordingAppConfigService();
        var runtime = new RecordingDataMapRuntime();
        using var viewModel = new RouteMapSignalMappingViewModel(
            scope.Manager,
            monitor,
            config,
            new ModbusDataMapValidator(),
            runtime);
        var row = viewModel.Rows.Single(item => item.SignalId == "system.emergency");
        row.IsMapped = true;
        row.Address = 0;

        await viewModel.SaveAsync();

        Assert.NotNull(config.SavedModbus);
        Assert.Contains(config.SavedModbus.DataMap, point => point.Name == "diagnostic.unrelated");
        Assert.Contains(config.SavedModbus.DataMap, point => point.Name == "system.emergency");
        Assert.Contains(runtime.Applied, point => point.Name == "system.emergency");
        Assert.False(viewModel.IsDirty);
    }

    private sealed class ManualSignalProvider : ISignalValueProvider
    {
        private readonly List<IObserver<IReadOnlyDictionary<string, SignalValue>>> _observers = [];

        public IObservable<IReadOnlyDictionary<string, SignalValue>> Observe() =>
            new ManualObservable(_observers);

        public void Publish(string source)
        {
            var snapshot = new Dictionary<string, SignalValue>
            {
                ["source"] = new("source", source, SignalValueType.String, DateTimeOffset.Now, true, false)
            };
            foreach (var observer in _observers.ToArray())
            {
                observer.OnNext(snapshot);
            }
        }

        private sealed class ManualObservable(
            List<IObserver<IReadOnlyDictionary<string, SignalValue>>> observers)
            : IObservable<IReadOnlyDictionary<string, SignalValue>>
        {
            public IDisposable Subscribe(IObserver<IReadOnlyDictionary<string, SignalValue>> observer)
            {
                observers.Add(observer);
                return new ActionDisposable(() => observers.Remove(observer));
            }
        }
    }

    private sealed class RecordingCommandDispatcher : IEquipmentCommandDispatcher
    {
        public List<SignalWriteRequest> Requests { get; } = [];

        public Task DispatchAsync(SignalWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDataMapRuntime : IModbusDataMapRuntime
    {
        public IReadOnlyList<ModbusDataPointOptions> Applied { get; private set; } = [];

        public ModbusOperationResult ApplyDataMap(IReadOnlyList<ModbusDataPointOptions> dataMap)
        {
            Applied = dataMap.Select(point => point.Clone()).ToArray();
            return ModbusOperationResult.Success();
        }
    }

    private sealed class TestOptionsMonitor(ModbusOptions value, ModbusOptions? demoValue = null) : IOptionsMonitor<ModbusOptions>
    {
        public ModbusOptions CurrentValue { get; private set; } = value;
        public ModbusOptions Get(string? name) =>
            string.Equals(name, ModbusOptions.DemoSectionName, StringComparison.Ordinal)
                ? demoValue ?? CurrentValue
                : CurrentValue;
        public IDisposable? OnChange(Action<ModbusOptions, string?> listener) => null;
    }

    private sealed class RecordingAppConfigService : IAppConfigService
    {
        public RouteMapRuntimeOptions? SavedRouteMapRuntime { get; private set; }
        public ModbusOptions? SavedModbus { get; private set; }

        public T GetSection<T>(string sectionName) where T : class, new()
        {
            if (typeof(T) == typeof(RouteMapRuntimeOptions))
            {
                return (T)(object)new RouteMapRuntimeOptions();
            }

            return new T();
        }

        public string GetValue(string key) => string.Empty;

        public Task SaveSectionAsync<T>(string sectionName, T value, CancellationToken ct = default)
        {
            if (value is RouteMapRuntimeOptions routeMapRuntime)
            {
                SavedRouteMapRuntime = new RouteMapRuntimeOptions
                {
                    SignalSource = routeMapRuntime.SignalSource,
                    StaleAfterMs = routeMapRuntime.StaleAfterMs
                };
            }
            else if (value is ModbusOptions modbus)
            {
                SavedModbus = modbus.Clone();
            }

            return Task.CompletedTask;
        }

        public void SaveUserSettings(UserSettings settings) { }
        public UserSettings LoadUserSettings() => new();
    }

    private sealed class NullFilePicker : IRouteMapSettingsFilePicker
    {
        public Task<string?> PickImportPathAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> PickExportPathAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class ConfigurationScope : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"route-map-signal-{Guid.NewGuid():N}");

        public ConfigurationScope()
        {
            Directory.CreateDirectory(_directory);
            Storage = new RouteMapConfigurationStorage(Path.Combine(_directory, "route-map.json"));
            var mapper = new RouteMapConfigurationMapper(RouteMapSeed.Create());
            Manager = new RouteMapConfigurationManager(
                Storage,
                mapper,
                new RouteMapConfigurationValidator(),
                new RouteMapConfigurationMigrator());
        }

        public RouteMapConfigurationStorage Storage { get; }
        public RouteMapConfigurationManager Manager { get; }

        public void Dispose()
        {
            Manager.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class ActionDisposable(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
