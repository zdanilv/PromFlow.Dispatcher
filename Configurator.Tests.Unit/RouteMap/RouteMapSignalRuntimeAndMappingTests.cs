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
using Microsoft.Extensions.Logging;
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
        Assert.True(inventory.Single(item => item.SignalId == "connection.connected").IsSystem);
        Assert.Equal(SignalValueType.Bool, inventory.Single(item => item.SignalId == "connection.connected").ExpectedType);
        var globalFault = inventory.Single(item => item.SignalId == RouteMapSystemSignalIds.GlobalFault);
        Assert.False(globalFault.IsSystem);
        Assert.Equal(RouteMapSignalElementCategory.System, globalFault.Category);
        Assert.Equal(SignalValueType.Bool, globalFault.ExpectedType);
        Assert.Equal(ModbusDataAccess.Read, globalFault.RequiredAccess);
        Assert.Equal(inventory.Count, inventory.Select(item => item.SignalId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void SignalInventory_AddsEquipmentCardParameters()
    {
        var seed = RouteMapSeed.Create();
        var card = seed.MapEquipment.Single();
        var definition = seed with
        {
            MapEquipment =
            [
                card with
                {
                    Parameters =
                    [
                        new EquipmentCardParameter(
                            "Скорость",
                            new SignalBinding(
                                SignalBindingRole.EquipmentParameter,
                                "equip.bucket.speed",
                                SignalBindingDirection.ReadWrite,
                                SignalValueType.Float32))
                    ]
                }
            ]
        };

        var item = RouteMapSignalInventory.Build(definition).Single(x => x.SignalId == "equip.bucket.speed");

        Assert.Equal(RouteMapSignalElementCategory.Card, item.Category);
        Assert.Equal(SignalValueType.Float32, item.ExpectedType);
        Assert.Equal(ModbusDataAccess.ReadWrite, item.RequiredAccess);
        Assert.Equal(nameof(SignalBindingRole.EquipmentParameter), item.Roles);
        Assert.Contains("Карточка equip.bucket", item.Objects);
        Assert.Contains("Скорость", item.Objects);
    }

    [Fact]
    public void SignalInventory_exposes_selector_commands_and_enabled_bindings()
    {
        var seed = RouteMapSeed.Create();
        var card = seed.MapEquipment.Single();
        var definition = seed with
        {
            TopBar = seed.TopBar! with
            {
                Automatic = seed.TopBar.Automatic with
                {
                    EnabledBinding = new SignalBinding(
                        SignalBindingRole.Enabled,
                        "system.mode.automatic.enabled",
                        SignalBindingDirection.Read,
                        SignalValueType.Bool)
                }
            },
            MapEquipment =
            [
                card with
                {
                    Bindings = card.Bindings.Append(new SignalBinding(
                        SignalBindingRole.Enabled,
                        "equip.bucket.enabled",
                        SignalBindingDirection.Read,
                        SignalValueType.Bool)).ToArray()
                }
            ]
        };

        var inventory = RouteMapSignalInventory.Build(definition);
        var uncheckedItem = inventory.Single(x => x.SignalId == "equip.bucket.selector.off");
        var checkedItem = inventory.Single(x => x.SignalId == "equip.bucket.selector.on");
        var resetItem = inventory.Single(x => x.SignalId == "system.reset");
        var cardEnabled = inventory.Single(x => x.SignalId == "equip.bucket.enabled");
        var topBarEnabled = inventory.Single(x => x.SignalId == "system.mode.automatic.enabled");

        Assert.Equal(ModbusDataAccess.ReadWrite, uncheckedItem.RequiredAccess);
        Assert.True(uncheckedItem.PreferHoldingRegisterBit);
        Assert.Equal(0, uncheckedItem.PreferredBitIndex);
        Assert.True(uncheckedItem.RequiresLatchedWriteMode);
        Assert.Equal(1, checkedItem.PreferredBitIndex);
        Assert.Equal(RouteMapSignalElementCategory.TopBar, resetItem.Category);
        Assert.Equal(SignalValueType.Bool, resetItem.ExpectedType);
        Assert.Equal(ModbusDataAccess.ReadWrite, resetItem.RequiredAccess);
        Assert.False(resetItem.RequiresLatchedWriteMode);
        Assert.True(resetItem.PreferPulseWriteMode);
        Assert.True(resetItem.RequiresPulseWriteMode);
        Assert.Contains(nameof(SignalBindingRole.ResetCommand), resetItem.Roles);
        Assert.Equal(ModbusDataAccess.Read, cardEnabled.RequiredAccess);
        Assert.Equal(ModbusDataAccess.Read, topBarEnabled.RequiredAccess);
        Assert.Contains(nameof(SignalBindingRole.Enabled), topBarEnabled.Roles);
    }

    [Fact]
    public void Mapping_selector_defaults_to_register_bits_and_rejects_pulse_but_allows_coil_and_reset_requires_pulse()
    {
        using var scope = new ConfigurationScope();
        using var viewModel = new RouteMapSignalMappingViewModel(
            scope.Manager,
            new TestOptionsMonitor(new ModbusOptions()),
            new RecordingAppConfigService(),
            new ModbusDataMapValidator(),
            new RecordingDataMapRuntime());
        var uncheckedRow = viewModel.Rows.Single(x => x.SignalId == "equip.bucket.selector.off");
        var checkedRow = viewModel.Rows.Single(x => x.SignalId == "equip.bucket.selector.on");

        viewModel.CreateMappingCommand.Execute(uncheckedRow).Subscribe();
        viewModel.CreateMappingCommand.Execute(checkedRow).Subscribe();

        Assert.Equal(ModbusDataArea.HoldingRegister, uncheckedRow.Area);
        Assert.Equal(0, uncheckedRow.BitIndex);
        Assert.Equal(ModbusDataArea.HoldingRegister, checkedRow.Area);
        Assert.Equal(1, checkedRow.BitIndex);
        Assert.Equal(ModbusWriteMode.Latched, checkedRow.WriteMode);

        checkedRow.WriteMode = ModbusWriteMode.Pulse;
        Assert.Contains("Latched", checkedRow.ValidationMessage);

        checkedRow.WriteMode = ModbusWriteMode.Latched;
        checkedRow.Area = ModbusDataArea.Coil;
        checkedRow.Address = 2;
        Assert.False(checkedRow.HasError);
        Assert.Null(checkedRow.BitIndex);

        var resetRow = viewModel.Rows.Single(x => x.SignalId == "system.reset");
        viewModel.CreateMappingCommand.Execute(resetRow).Subscribe();
        Assert.Equal(ModbusWriteMode.Pulse, resetRow.WriteMode);
        Assert.Equal(300, resetRow.PulseDurationMs);
        resetRow.WriteMode = ModbusWriteMode.Latched;
        Assert.Contains("Pulse", resetRow.ValidationMessage);
    }

    [Fact]
    public void SignalInventory_TreatsWordAndUInt16AsCompatible()
    {
        var seed = RouteMapSeed.Create();
        var card = seed.MapEquipment.Single();
        var definition = seed with
        {
            MapEquipment =
            [
                card with
                {
                    Parameters =
                    [
                        new EquipmentCardParameter(
                            "WORD",
                            new SignalBinding(
                                SignalBindingRole.EquipmentParameter,
                                "equip.bucket.word",
                                SignalBindingDirection.ReadWrite,
                                SignalValueType.Word)),
                        new EquipmentCardParameter(
                            "UInt16",
                            new SignalBinding(
                                SignalBindingRole.EquipmentParameter,
                                "equip.bucket.word",
                                SignalBindingDirection.Read,
                                SignalValueType.UInt16))
                    ]
                }
            ]
        };

        var item = RouteMapSignalInventory.Build(definition).Single(x => x.SignalId == "equip.bucket.word");

        Assert.False(item.HasTypeConflict);
        Assert.Equal(SignalValueType.Word, item.ExpectedType);
        Assert.Equal(ModbusDataAccess.ReadWrite, item.RequiredAccess);
    }

    [Theory]
    [InlineData(SignalValueType.Word, ModbusValueType.Word, 1)]
    [InlineData(SignalValueType.Dword, ModbusValueType.Dword, 2)]
    [InlineData(SignalValueType.Date, ModbusValueType.Date, 2)]
    [InlineData(SignalValueType.String, ModbusValueType.String, 1)]
    public void MappingRow_CreatesDefaultPointsForNewSignalTypes(
        SignalValueType signalType,
        ModbusValueType modbusType,
        int length)
    {
        var item = new RouteMapSignalInventoryItem(
            $"test.{signalType}",
            signalType,
            ModbusDataAccess.ReadWrite,
            "EquipmentParameter",
            "Card",
            HasTypeConflict: false,
            Category: RouteMapSignalElementCategory.Card);

        var point = RouteMapSignalMappingRow.CreateDefaultPoint(item);

        Assert.Equal(ModbusDataArea.HoldingRegister, point.Area);
        Assert.Equal(modbusType, point.Type);
        Assert.Equal(length, point.Length);
        Assert.Equal(ModbusDataAccess.ReadWrite, point.Access);
    }

    [Theory]
    [InlineData(SignalValueType.Word, ModbusValueType.Word, true)]
    [InlineData(SignalValueType.Word, ModbusValueType.UInt16, true)]
    [InlineData(SignalValueType.UInt16, ModbusValueType.Word, true)]
    [InlineData(SignalValueType.UInt16, ModbusValueType.UInt16, true)]
    [InlineData(SignalValueType.Dword, ModbusValueType.Dword, true)]
    [InlineData(SignalValueType.Date, ModbusValueType.Date, true)]
    [InlineData(SignalValueType.Date, ModbusValueType.Dword, false)]
    public void SignalModbusTypeCompatibility_ChecksNewTypes(
        SignalValueType signalType,
        ModbusValueType modbusType,
        bool expected)
    {
        Assert.Equal(expected, SignalModbusTypeCompatibility.IsCompatible(signalType, modbusType));
    }

    [Fact]
    public void MappingRow_NormalizesFixedTypeLengths()
    {
        var item = new RouteMapSignalInventoryItem(
            "test.dword",
            SignalValueType.Dword,
            ModbusDataAccess.ReadWrite,
            "EquipmentParameter",
            "Card",
            HasTypeConflict: false,
            Category: RouteMapSignalElementCategory.Card);
        var row = new RouteMapSignalMappingRow(
            item,
            new ModbusDataPointOptions
            {
                Name = item.SignalId,
                Area = ModbusDataArea.HoldingRegister,
                Address = 0,
                Length = 1,
                Access = ModbusDataAccess.ReadWrite,
                Type = ModbusValueType.Dword
            },
            isMapped: true);

        Assert.Equal(2, row.Length);

        row.Type = ModbusValueType.Word;
        Assert.Equal(1, row.Length);

        row.Type = ModbusValueType.Date;
        Assert.Equal(2, row.Length);

        row.Type = ModbusValueType.Real;
        Assert.Equal(2, row.Length);
    }

    [Fact]
    public void MappingRow_PreservesManualStringLength()
    {
        var item = new RouteMapSignalInventoryItem(
            "test.string",
            SignalValueType.String,
            ModbusDataAccess.ReadWrite,
            "EquipmentParameter",
            "Card",
            HasTypeConflict: false,
            Category: RouteMapSignalElementCategory.Card);
        var row = new RouteMapSignalMappingRow(
            item,
            new ModbusDataPointOptions
            {
                Name = item.SignalId,
                Area = ModbusDataArea.HoldingRegister,
                Address = 0,
                Length = 8,
                Access = ModbusDataAccess.ReadWrite,
                Type = ModbusValueType.String
            },
            isMapped: true);

        row.Address = 12;

        Assert.Equal(8, row.Length);

        row.Length = 0;

        Assert.Equal(1, row.Length);
    }

    [Fact]
    public void SignalInventory_AssignsEveryElementCategory()
    {
        var definition = RouteMapSeed.Create();
        var binding = new SignalBinding(
            SignalBindingRole.Visible,
            "test.vehicle.visible",
            SignalBindingDirection.Read,
            SignalValueType.Bool);
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
    public void SignalInventory_IncludesSegmentActiveFragmentBindings()
    {
        var definition = RouteMapSeed.Create();

        var inventory = RouteMapSignalInventory.Build(definition);
        var item = inventory.Single(row => row.SignalId == "route.bsu2_to_bucket.fragment_2.active");

        Assert.Equal(SignalValueType.Bool, item.ExpectedType);
        Assert.Equal(ModbusDataAccess.Read, item.RequiredAccess);
        Assert.Equal(RouteMapSignalElementCategory.Segment, item.Category);
        Assert.Contains(nameof(SignalBindingRole.ActiveRouteFragment), item.Roles);
        Assert.Contains("отрезок 2", item.Objects);
    }

    [Fact]
    public void ModbusBindingDiagnostics_IncludesSegmentActiveFragmentBindings()
    {
        using var scope = new ConfigurationScope();
        using var runtime = new RouteMapSignalRuntime(
            new ManualSignalProvider(),
            new RecordingCommandDispatcher(),
            new ManualSignalProvider(),
            new RecordingCommandDispatcher(),
            RouteMapSignalSource.Modbus);
        var logger = new CapturingLogger<RouteMapModbusBindingDiagnostics>();

        using var diagnostics = new RouteMapModbusBindingDiagnostics(
            scope.Manager,
            new TestOptionsMonitor(new ModbusOptions()),
            runtime,
            logger);

        Assert.Contains(
            logger.Warnings,
            message => message.Contains("route.bsu2_to_bucket.fragment_2.active", StringComparison.Ordinal));
    }

    [Fact]
    public void SignalInventory_DoesNotExposeLegacyStateSignals()
    {
        var seed = RouteMapSeed.Create();
        var node = seed.Nodes.Single(x => x.Id == "bsu_1");
        var segment = seed.Segments.Single(x => x.Id == "bsu2_to_bucket");
        var card = seed.MapEquipment.Single();
        var definition = seed with
        {
            Nodes = seed.Nodes.Select(x => x.Id == node.Id
                ? x with
                {
                    Bindings = node.Bindings.Concat(
                    [
                        new SignalBinding(
                            SignalBindingRole.State,
                            "bsu_1.state",
                            SignalBindingDirection.Read,
                            SignalValueType.String)
                    ]).ToArray()
                }
                : x).ToArray(),
            Segments = seed.Segments.Select(x => x.Id == segment.Id
                ? x with
                {
                    Bindings = segment.Bindings.Concat(
                    [
                        new SignalBinding(
                            SignalBindingRole.State,
                            "bsu2_to_bucket.state",
                            SignalBindingDirection.Read,
                            SignalValueType.String)
                    ]).ToArray()
                }
                : x).ToArray(),
            MapEquipment =
            [
                card with
                {
                    Bindings = card.Bindings.Concat(
                    [
                        new SignalBinding(
                            SignalBindingRole.State,
                            "equip.bucket.state",
                            SignalBindingDirection.Read,
                            SignalValueType.String)
                    ]).ToArray()
                }
            ]
        };

        var inventory = RouteMapSignalInventory.Build(definition);

        Assert.DoesNotContain(inventory, item => item.SignalId.EndsWith(".state", StringComparison.OrdinalIgnoreCase));
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
    public void MappingDefaults_UseLatchedForToggleCommandsAndDemoAddressBases()
    {
        using var scope = new ConfigurationScope();
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

        Assert.Equal(ModbusWriteMode.Latched, row.WriteMode);
        Assert.Equal("100", row.ClientPhysicalAddress);
        Assert.Equal("100", row.ClientPhysicalAddressText);
        Assert.Equal("300", row.ServerPhysicalAddress);
        Assert.Equal("300", row.ServerPhysicalAddressText);
    }

    [Fact]
    public void MappingRow_PhysicalAddressText_RecalculatesSharedOffset()
    {
        using var scope = new ConfigurationScope();
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
        var row = viewModel.Rows.Single(item => item.SignalId == "system.emergency");
        viewModel.CreateMappingCommand.Execute(row).Subscribe();
        row.Area = ModbusDataArea.HoldingRegister;
        row.Type = ModbusValueType.Bool;

        row.ClientPhysicalAddressText = "205";

        Assert.Equal(5, row.Address);
        Assert.Equal("205", row.ClientPhysicalAddressText);
        Assert.Equal("405", row.ServerPhysicalAddressText);

        row.ServerPhysicalAddressText = "407";

        Assert.Equal(7, row.Address);
        Assert.Equal("207", row.ClientPhysicalAddressText);
        Assert.Equal("407", row.ServerPhysicalAddressText);

        row.Address = 9;

        Assert.Equal("209", row.ClientPhysicalAddressText);
        Assert.Equal("409", row.ServerPhysicalAddressText);
    }

    [Fact]
    public void MappingRow_NodeBoolPhysicalRegisterAddress_SwitchesAreaAndEnablesBit()
    {
        using var scope = new ConfigurationScope();
        var routeOptions = new ModbusOptions();
        var demoOptions = new ModbusOptions
        {
            Client = new ModbusEndpointOptions
            {
                CoilStartAddress = 0,
                CoilCount = 2000,
                HoldingRegisterStartAddress = 16384,
                RegisterCount = 100
            },
            Server = new ModbusEndpointOptions
            {
                CoilStartAddress = 0,
                CoilCount = 2000,
                HoldingRegisterStartAddress = 16384,
                RegisterCount = 100
            }
        };
        using var viewModel = new RouteMapSignalMappingViewModel(
            scope.Manager,
            new TestOptionsMonitor(routeOptions, demoOptions),
            new RecordingAppConfigService(),
            new ModbusDataMapValidator(),
            new RecordingDataMapRuntime());
        var row = viewModel.Rows.Single(item => item.SignalId == "bsu_1.fault");
        viewModel.CreateMappingCommand.Execute(row).Subscribe();

        Assert.Equal(ModbusDataArea.Coil, row.Area);
        Assert.Null(row.BitIndex);
        Assert.False(row.CanEditBitIndex);

        row.ClientPhysicalAddressText = "16420";

        Assert.Equal(ModbusDataArea.HoldingRegister, row.Area);
        Assert.Equal(36, row.Address);
        Assert.Equal(0, row.BitIndex);
        Assert.True(row.CanEditBitIndex);
        Assert.Equal("16420", row.ClientPhysicalAddressText);
        Assert.Equal("16420", row.ServerPhysicalAddressText);
        Assert.False(row.HasError);

        row.BitIndex = 2;

        var point = row.ToOptions();
        Assert.Equal(ModbusDataArea.HoldingRegister, point.Area);
        Assert.Equal(36, point.Address);
        Assert.Equal(2, point.BitIndex);
    }

    [Theory]
    [InlineData("active_bsu1_bsu2.fault")]
    [InlineData("route.active_bsu1_bsu2.active")]
    [InlineData("equip.bucket.start")]
    [InlineData("equip.bucket.stop")]
    public void MappingRow_LineAndCardBoolPhysicalRegisterAddress_SwitchesAreaAndEnablesBit(string signalId)
    {
        using var scope = new ConfigurationScope();
        var routeOptions = new ModbusOptions();
        var demoOptions = CreateRegisterAddressDemoOptions();
        using var viewModel = new RouteMapSignalMappingViewModel(
            scope.Manager,
            new TestOptionsMonitor(routeOptions, demoOptions),
            new RecordingAppConfigService(),
            new ModbusDataMapValidator(),
            new RecordingDataMapRuntime());
        var row = viewModel.Rows.Single(item => item.SignalId == signalId);
        viewModel.CreateMappingCommand.Execute(row).Subscribe();

        row.ClientPhysicalAddressText = "16420";
        row.BitIndex = 5;

        Assert.Equal(ModbusDataArea.HoldingRegister, row.Area);
        Assert.Equal(36, row.Address);
        Assert.Equal(5, row.BitIndex);
        Assert.True(row.CanEditBitIndex);
        Assert.Equal("16420", row.ClientPhysicalAddressText);

        var point = row.ToOptions();
        Assert.Equal(ModbusDataArea.HoldingRegister, point.Area);
        Assert.Equal(36, point.Address);
        Assert.Equal(5, point.BitIndex);
    }

    [Fact]
    public void MappingRow_CardTextPhysicalRegisterAddress_KeepsRegisterWithoutBit()
    {
        using var scope = new ConfigurationScope();
        var routeOptions = new ModbusOptions();
        var demoOptions = CreateRegisterAddressDemoOptions();
        using var viewModel = new RouteMapSignalMappingViewModel(
            scope.Manager,
            new TestOptionsMonitor(routeOptions, demoOptions),
            new RecordingAppConfigService(),
            new ModbusDataMapValidator(),
            new RecordingDataMapRuntime());
        var row = viewModel.Rows.Single(item => item.SignalId == "equip.bucket.text");
        viewModel.CreateMappingCommand.Execute(row).Subscribe();

        row.ClientPhysicalAddressText = "16421";

        Assert.Equal(ModbusDataArea.HoldingRegister, row.Area);
        Assert.Equal(37, row.Address);
        Assert.Null(row.BitIndex);
        Assert.False(row.CanEditBitIndex);
        Assert.Equal("16421", row.ClientPhysicalAddressText);
    }

    [Fact]
    public void MappingRow_ServerPhysicalRegisterAddress_RecalculatesOffset()
    {
        using var scope = new ConfigurationScope();
        var routeOptions = new ModbusOptions();
        var demoOptions = new ModbusOptions
        {
            Client = new ModbusEndpointOptions
            {
                CoilStartAddress = 0,
                CoilCount = 200,
                HoldingRegisterStartAddress = 100,
                RegisterCount = 100
            },
            Server = new ModbusEndpointOptions
            {
                CoilStartAddress = 0,
                CoilCount = 200,
                HoldingRegisterStartAddress = 300,
                RegisterCount = 100
            }
        };
        using var viewModel = new RouteMapSignalMappingViewModel(
            scope.Manager,
            new TestOptionsMonitor(routeOptions, demoOptions),
            new RecordingAppConfigService(),
            new ModbusDataMapValidator(),
            new RecordingDataMapRuntime());
        var row = viewModel.Rows.Single(item => item.SignalId == "bsu_1.fault");
        viewModel.CreateMappingCommand.Execute(row).Subscribe();

        row.ServerPhysicalAddressText = "336";

        Assert.Equal(ModbusDataArea.HoldingRegister, row.Area);
        Assert.Equal(36, row.Address);
        Assert.Equal(0, row.BitIndex);
        Assert.True(row.CanEditBitIndex);
        Assert.Equal("136", row.ClientPhysicalAddressText);
        Assert.Equal("336", row.ServerPhysicalAddressText);
    }

    [Fact]
    public void MappingRow_PhysicalCoilAddress_SwitchesAreaAndClearsBit()
    {
        using var scope = new ConfigurationScope();
        var routeOptions = new ModbusOptions();
        var demoOptions = new ModbusOptions
        {
            Client = new ModbusEndpointOptions
            {
                CoilStartAddress = 0,
                CoilCount = 2000,
                HoldingRegisterStartAddress = 16384,
                RegisterCount = 100
            },
            Server = new ModbusEndpointOptions
            {
                CoilStartAddress = 0,
                CoilCount = 2000,
                HoldingRegisterStartAddress = 16384,
                RegisterCount = 100
            }
        };
        using var viewModel = new RouteMapSignalMappingViewModel(
            scope.Manager,
            new TestOptionsMonitor(routeOptions, demoOptions),
            new RecordingAppConfigService(),
            new ModbusDataMapValidator(),
            new RecordingDataMapRuntime());
        var row = viewModel.Rows.Single(item => item.SignalId == "bsu_1.fault");
        viewModel.CreateMappingCommand.Execute(row).Subscribe();
        row.ClientPhysicalAddressText = "16420";
        row.BitIndex = 2;

        row.ClientPhysicalAddressText = "25";

        Assert.Equal(ModbusDataArea.Coil, row.Area);
        Assert.Equal(25, row.Address);
        Assert.Null(row.BitIndex);
        Assert.False(row.CanEditBitIndex);
    }

    [Fact]
    public void MappingRow_PhysicalAddressOutsideEndpointRange_ShowsValidationWithoutChangingOffset()
    {
        using var scope = new ConfigurationScope();
        var routeOptions = new ModbusOptions();
        var demoOptions = new ModbusOptions
        {
            Client = new ModbusEndpointOptions
            {
                CoilStartAddress = 0,
                CoilCount = 2000,
                HoldingRegisterStartAddress = 16384,
                RegisterCount = 100
            },
            Server = new ModbusEndpointOptions
            {
                CoilStartAddress = 0,
                CoilCount = 2000,
                HoldingRegisterStartAddress = 16384,
                RegisterCount = 100
            }
        };
        using var viewModel = new RouteMapSignalMappingViewModel(
            scope.Manager,
            new TestOptionsMonitor(routeOptions, demoOptions),
            new RecordingAppConfigService(),
            new ModbusDataMapValidator(),
            new RecordingDataMapRuntime());
        var row = viewModel.Rows.Single(item => item.SignalId == "bsu_1.fault");
        viewModel.CreateMappingCommand.Execute(row).Subscribe();

        row.ClientPhysicalAddressText = "50000";

        Assert.Equal(ModbusDataArea.Coil, row.Area);
        Assert.Equal(0, row.Address);
        Assert.Contains("не входит в диапазоны endpoint", row.ValidationMessage);
    }

    [Fact]
    public void MappingRow_NormalizesBitIndexForAreaAndType()
    {
        using var scope = new ConfigurationScope();
        using var viewModel = new RouteMapSignalMappingViewModel(
            scope.Manager,
            new TestOptionsMonitor(new ModbusOptions()),
            new RecordingAppConfigService(),
            new ModbusDataMapValidator(),
            new RecordingDataMapRuntime());
        var row = viewModel.Rows.Single(item => item.SignalId == "system.emergency");
        viewModel.CreateMappingCommand.Execute(row).Subscribe();

        Assert.Equal(ModbusDataArea.Coil, row.Area);
        Assert.Null(row.BitIndex);
        Assert.False(row.CanEditBitIndex);

        row.Area = ModbusDataArea.HoldingRegister;

        Assert.Equal(0, row.BitIndex);
        Assert.True(row.CanEditBitIndex);

        row.BitIndex = 7;
        row.Area = ModbusDataArea.Coil;

        Assert.Null(row.BitIndex);
        Assert.False(row.CanEditBitIndex);

        row.Area = ModbusDataArea.HoldingRegister;
        row.Type = ModbusValueType.UInt16;

        Assert.Null(row.BitIndex);
        Assert.False(row.CanEditBitIndex);

        row.Type = ModbusValueType.Bool;

        Assert.Equal(0, row.BitIndex);
        Assert.True(row.CanEditBitIndex);
    }

    [Fact]
    public void SignalInventory_DoesNotExposeLegacyOffFeedbackSignals()
    {
        var definition = RouteMapSeed.Create();

        var inventory = RouteMapSignalInventory.Build(definition);

        Assert.DoesNotContain(inventory, item => item.SignalId == "equip.bucket.start.off");
        Assert.DoesNotContain(inventory, item => item.SignalId == "equip.bucket.stop.off");
        Assert.DoesNotContain(inventory, item => item.SignalId == "system.emergency.off");
        Assert.DoesNotContain(inventory, item => item.SignalId == "system.mode.manual.off");
        Assert.DoesNotContain(inventory, item => item.SignalId == "route.node.bsu_1.loader.off");
    }

    [Fact]
    public void SignalInventory_ExposesCardStartStopOffFeedbackSignals()
    {
        var seed = RouteMapSeed.Create();
        var card = seed.MapEquipment.Single();
        var definition = seed with
        {
            MapEquipment =
            [
                card with
                {
                    Bindings = card.Bindings.Concat(
                    [
                        new SignalBinding(SignalBindingRole.StartOffFeedback, "equip.bucket.start.off", SignalBindingDirection.Read, SignalValueType.Bool),
                        new SignalBinding(SignalBindingRole.StopOffFeedback, "equip.bucket.stop.off", SignalBindingDirection.Read, SignalValueType.Bool)
                    ]).ToArray()
                }
            ]
        };

        var inventory = RouteMapSignalInventory.Build(definition);

        Assert.Contains(inventory, item => item.SignalId == "equip.bucket.start.off");
        Assert.Contains(inventory, item => item.SignalId == "equip.bucket.stop.off");
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
                },
                new ModbusDataPointOptions
                {
                    Name = "equip.bucket.state",
                    Area = ModbusDataArea.HoldingRegister,
                    Address = 8,
                    Length = 1,
                    Type = ModbusValueType.UInt16,
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
        Assert.Contains(config.SavedModbus.DataMap, point => point.Name == "equip.bucket.state");
        Assert.Contains(config.SavedModbus.DataMap, point => point.Name == "system.emergency");
        Assert.Contains(runtime.Applied, point => point.Name == "system.emergency");
        Assert.False(viewModel.IsDirty);
    }

    [Fact]
    public async Task MappingSave_PersistsOffsetAndBitFromPhysicalAddress()
    {
        using var scope = new ConfigurationScope();
        var routeOptions = new ModbusOptions();
        var demoOptions = new ModbusOptions
        {
            Client = new ModbusEndpointOptions
            {
                HoldingRegisterStartAddress = 100,
                RegisterCount = 20
            },
            Server = new ModbusEndpointOptions
            {
                HoldingRegisterStartAddress = 300,
                RegisterCount = 20
            }
        };
        var config = new RecordingAppConfigService();
        using var viewModel = new RouteMapSignalMappingViewModel(
            scope.Manager,
            new TestOptionsMonitor(routeOptions, demoOptions),
            config,
            new ModbusDataMapValidator(),
            new RecordingDataMapRuntime());
        var row = viewModel.Rows.Single(item => item.SignalId == "system.emergency");
        viewModel.CreateMappingCommand.Execute(row).Subscribe();
        row.Area = ModbusDataArea.HoldingRegister;
        row.Type = ModbusValueType.Bool;
        row.BitIndex = 4;
        row.ClientPhysicalAddressText = "105";

        await viewModel.SaveAsync();

        var point = Assert.Single(config.SavedModbus!.DataMap, point => point.Name == "system.emergency");
        Assert.Equal(5, point.Address);
        Assert.Equal(4, point.BitIndex);
        Assert.Equal(ModbusDataArea.HoldingRegister, point.Area);
    }

    [Fact]
    public async Task MappingSave_PersistsGlobalFaultSignal()
    {
        using var scope = new ConfigurationScope();
        var config = new RecordingAppConfigService();
        using var viewModel = new RouteMapSignalMappingViewModel(
            scope.Manager,
            new TestOptionsMonitor(new ModbusOptions()),
            config,
            new ModbusDataMapValidator(),
            new RecordingDataMapRuntime());
        var row = viewModel.Rows.Single(item => item.SignalId == RouteMapSystemSignalIds.GlobalFault);

        viewModel.CreateMappingCommand.Execute(row).Subscribe();
        await viewModel.SaveAsync();

        Assert.False(row.IsSystem);
        var point = Assert.Single(config.SavedModbus!.DataMap, point => point.Name == RouteMapSystemSignalIds.GlobalFault);
        Assert.Equal(ModbusDataAccess.Read, point.Access);
        Assert.Equal(ModbusValueType.Bool, point.Type);
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

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }

    private static ModbusOptions CreateRegisterAddressDemoOptions() => new()
    {
        Client = new ModbusEndpointOptions
        {
            CoilStartAddress = 0,
            CoilCount = 2000,
            HoldingRegisterStartAddress = 16384,
            RegisterCount = 100
        },
        Server = new ModbusEndpointOptions
        {
            CoilStartAddress = 0,
            CoilCount = 2000,
            HoldingRegisterStartAddress = 16384,
            RegisterCount = 100
        }
    };

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
