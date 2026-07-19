using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Configurator.Application.Services;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Desktop.Workspace.ModbusDemo;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed class ModbusAddressCatalogViewModelTests
{
    [Fact]
    public async Task Catalog_BuildsUnionOfActiveRangesAndPersistsOnlyAddressLabels()
    {
        var runtime = CreateRuntimeOptions();
        runtime.Client.CoilCount = 2;
        runtime.Client.RegisterCount = 1;
        runtime.Server.CoilCount = 3;
        runtime.Server.RegisterCount = 2;
        runtime.Client.CoilStartAddress = 100;
        runtime.Server.CoilStartAddress = 100;
        runtime.Client.HoldingRegisterStartAddress = 16384;
        runtime.Server.HoldingRegisterStartAddress = 17000;
        var map = new ModbusOptions
        {
            DataMap =
            [
                new ModbusDataPointOptions
                {
                    Name = "unrelated.point",
                    Area = ModbusDataArea.Coil,
                    Address = 1,
                    Type = ModbusValueType.Bool
                }
            ],
            AlarmMap =
            [
                new ModbusAlarmOptions
                {
                    Id = "alarm.drive",
                    Alarm = new ModbusBitAddressOptions
                    {
                        Area = ModbusDataArea.HoldingRegister,
                        Address = 0,
                        BitIndex = 2
                    },
                    Acknowledgement = new ModbusBitAddressOptions
                    {
                        Area = ModbusDataArea.Coil,
                        Address = 2
                    }
                }
            ],
            AddressLabels =
            [
                new ModbusAddressLabelOptions { Area = ModbusDataArea.Coil, Address = 2, DisplayName = "Подтверждение" },
                new ModbusAddressLabelOptions { Area = ModbusDataArea.HoldingRegister, Address = 0, BitIndex = 2, DisplayName = "Авария" }
            ]
        };
        var config = new RecordingAppConfigService(map);
        using var viewModel = new ModbusAddressCatalogViewModel(
            config,
            () => runtime,
            action => action(),
            new StaticOptionsMonitor(map));

        Assert.Equal(3, viewModel.CoilRows.Count);
        Assert.Equal(2, viewModel.HoldingRegisterRows.Count);
        Assert.Equal(16, viewModel.HoldingRegisterRows[0].Bits.Count);
        Assert.Equal("Подтверждение", viewModel.CoilRows[2].DisplayName);
        Assert.Equal("Авария", viewModel.HoldingRegisterRows[0].Bits[2].DisplayName);
        Assert.Equal("102", viewModel.CoilRows[2].FullAddressText);
        Assert.Equal("Client: 16384 / Server: 17000", viewModel.HoldingRegisterRows[0].FullAddressText);
        Assert.Equal("17001", viewModel.HoldingRegisterRows[1].FullAddressText);
        Assert.Equal("Да", viewModel.HoldingRegisterRows[0].UsageText);
        Assert.Equal("—", viewModel.HoldingRegisterRows[0].DescriptionText);
        Assert.Equal("Менеджер тревог", viewModel.CoilRows[2].UsageText);
        Assert.Contains("Alarm: alarm.drive", viewModel.HoldingRegisterRows[0].Bits[2].RoleText);

        await viewModel.ExpandAllCommand.Execute().FirstAsync().ToTask();
        Assert.All(viewModel.HoldingRegisterRows, row => Assert.True(row.IsExpanded));

        var register = viewModel.HoldingRegisterRows[0];
        await viewModel.ToggleEditCommand.Execute(register).FirstAsync().ToTask();
        register.EditDisplayName = "Слово состояния";
        await viewModel.ToggleEditCommand.Execute(register).FirstAsync().ToTask();
        Assert.True(register.IsExpanded);

        await viewModel.CollapseAllCommand.Execute().FirstAsync().ToTask();
        Assert.All(viewModel.HoldingRegisterRows, row => Assert.False(row.IsExpanded));

        register.IsExpanded = true;
        Assert.True(register.IsExpanded);

        var row = viewModel.CoilRows[2];
        await viewModel.ToggleEditCommand.Execute(row).FirstAsync().ToTask();
        Assert.True(row.IsEditing);
        row.EditDisplayName = "Подтверждение привода";
        await viewModel.ToggleEditCommand.Execute(row).FirstAsync().ToTask();

        var saved = Assert.IsType<ModbusOptions>(config.LastSavedSection);
        Assert.Single(saved.DataMap);
        Assert.Single(saved.AlarmMap);
        Assert.Equal("Подтверждение привода", Assert.Single(saved.AddressLabels, label => label.Area == ModbusDataArea.Coil).DisplayName);

        await viewModel.ToggleEditCommand.Execute(row).FirstAsync().ToTask();
        row.EditDisplayName = string.Empty;
        await viewModel.ToggleEditCommand.Execute(row).FirstAsync().ToTask();

        var afterRemoval = Assert.IsType<ModbusOptions>(config.LastSavedSection);
        Assert.DoesNotContain(afterRemoval.AddressLabels, label => label.Area == ModbusDataArea.Coil && label.Address == 2);
    }

    [Fact]
    public void Catalog_HoldingRegisterDescriptionContainsOnlyDirectSignalId()
    {
        var runtime = CreateRuntimeOptions();
        runtime.Client.RegisterCount = 1;
        runtime.Server.RegisterCount = 1;
        var map = new ModbusOptions
        {
            DataMap =
            [
                new ModbusDataPointOptions
                {
                    Name = "equip.bucket.text",
                    Area = ModbusDataArea.HoldingRegister,
                    Address = 0,
                    Length = 1,
                    Type = ModbusValueType.UInt16
                },
                new ModbusDataPointOptions
                {
                    Name = "equip.bucket.selector.on",
                    Area = ModbusDataArea.HoldingRegister,
                    Address = 0,
                    Length = 1,
                    Type = ModbusValueType.Bool,
                    BitIndex = 1
                }
            ]
        };
        using var routeMap = new RouteMapConfigurationScope();
        using var viewModel = new ModbusAddressCatalogViewModel(
            new RecordingAppConfigService(map),
            () => runtime,
            action => action(),
            new StaticOptionsMonitor(map),
            routeMap.Manager);

        var register = Assert.Single(viewModel.HoldingRegisterRows);

        Assert.Equal("Да", register.UsageText);
        Assert.Equal("equip.bucket.text", register.DescriptionText);
        Assert.Contains("equip.bucket.selector.on", register.Bits[1].SignalIdText);
        Assert.Equal("—", register.Bits[1].DescriptionText);
    }

    [Fact]
    public void Catalog_ExcludesDisabledAreas()
    {
        var runtime = CreateRuntimeOptions();
        runtime.Client.CoilsEnabled = false;
        runtime.Client.HoldingRegistersEnabled = false;
        runtime.Server.Enabled = false;
        var map = new ModbusOptions();

        using var viewModel = new ModbusAddressCatalogViewModel(
            new RecordingAppConfigService(map),
            () => runtime,
            action => action(),
            new StaticOptionsMonitor(map));

        Assert.Empty(viewModel.CoilRows);
        Assert.Empty(viewModel.HoldingRegisterRows);
    }

    [Fact]
    public void Catalog_UpdatesRegisterAndBitValuesFromRuntimeSnapshots()
    {
        var runtimeOptions = CreateRuntimeOptions();
        runtimeOptions.Client.CoilCount = 2;
        runtimeOptions.Client.RegisterCount = 1;
        runtimeOptions.Server.CoilCount = 2;
        runtimeOptions.Server.RegisterCount = 1;
        var map = new ModbusOptions();
        var snapshots = new SnapshotRuntimeService();

        using var viewModel = new ModbusAddressCatalogViewModel(
            new RecordingAppConfigService(map),
            () => runtimeOptions,
            action => action(),
            new StaticOptionsMonitor(map),
            runtimeService: snapshots);

        Assert.Equal("—", viewModel.CoilRows[0].CurrentValueText);
        Assert.Equal("—", viewModel.HoldingRegisterRows[0].CurrentValueText);
        Assert.Equal("—", viewModel.HoldingRegisterRows[0].Bits[0].CurrentValueText);

        snapshots.Publish(new ModbusSnapshot
        {
            Role = ModbusRuntimeRole.Client,
            Coils = [true, false],
            HoldingRegisters = [(ushort)5]
        });

        Assert.Equal("1", viewModel.CoilRows[0].CurrentValueText);
        Assert.Equal("0", viewModel.CoilRows[1].CurrentValueText);
        Assert.Equal("5", viewModel.HoldingRegisterRows[0].CurrentValueText);
        Assert.Equal("1", viewModel.HoldingRegisterRows[0].Bits[0].CurrentValueText);
        Assert.Equal("0", viewModel.HoldingRegisterRows[0].Bits[1].CurrentValueText);
        Assert.Equal("1", viewModel.HoldingRegisterRows[0].Bits[2].CurrentValueText);

        snapshots.Publish(new ModbusSnapshot
        {
            Role = ModbusRuntimeRole.Server,
            Coils = [false, false],
            HoldingRegisters = [(ushort)2]
        });

        Assert.Equal("Client: 1 / Server: 0", viewModel.CoilRows[0].CurrentValueText);
        Assert.Equal("Client: 5 / Server: 2", viewModel.HoldingRegisterRows[0].CurrentValueText);
        Assert.Equal("Client: 1 / Server: 0", viewModel.HoldingRegisterRows[0].Bits[0].CurrentValueText);
        Assert.Equal("Client: 0 / Server: 1", viewModel.HoldingRegisterRows[0].Bits[1].CurrentValueText);
    }

    private static ModbusOptions CreateRuntimeOptions() => new()
    {
        Client = new ModbusEndpointOptions
        {
            Enabled = true,
            CoilsEnabled = true,
            HoldingRegistersEnabled = true,
            CoilCount = 10,
            RegisterCount = 10
        },
        Server = new ModbusEndpointOptions
        {
            Enabled = true,
            CoilsEnabled = true,
            HoldingRegistersEnabled = true,
            CoilCount = 10,
            RegisterCount = 10
        }
    };

    private sealed class SnapshotRuntimeService : IModbusRuntimeService
    {
        public ModbusStatus Status { get; } = new();

        public ModbusSnapshot ClientSnapshot { get; private set; } = ModbusSnapshot.Empty;

        public ModbusSnapshot ServerSnapshot { get; private set; } = ModbusSnapshot.Empty;

        public ModbusOptions CurrentOptions { get; } = new();

        public event EventHandler<ModbusStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<ModbusSnapshot>? SnapshotChanged;

        public Task StartAsync(ModbusRunMode mode = ModbusRunMode.Both, ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RestartAsync(ModbusRunMode mode = ModbusRunMode.Both, ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopClientAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RestartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopServerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RestartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Publish(ModbusSnapshot snapshot)
        {
            switch (snapshot.Role)
            {
                case ModbusRuntimeRole.Client:
                    ClientSnapshot = snapshot;
                    break;
                case ModbusRuntimeRole.Server:
                    ServerSnapshot = snapshot;
                    break;
            }

            SnapshotChanged?.Invoke(this, snapshot);
        }
    }

    private sealed class StaticOptionsMonitor(ModbusOptions options) : IOptionsMonitor<ModbusOptions>
    {
        public ModbusOptions CurrentValue => options;

        public ModbusOptions Get(string? name) => options;

        public IDisposable OnChange(Action<ModbusOptions, string?> listener) => Disposable.Empty;
    }

    private sealed class RecordingAppConfigService(ModbusOptions options) : IAppConfigService
    {
        public object? LastSavedSection { get; private set; }

        public T GetSection<T>(string sectionName) where T : class, new() =>
            options.Clone() as T ?? new T();

        public string GetValue(string key) => string.Empty;

        public Task SaveSectionAsync<T>(string sectionName, T value, CancellationToken ct = default)
        {
            LastSavedSection = value;
            return Task.CompletedTask;
        }

        public void SaveUserSettings(UserSettings settings)
        {
        }

        public UserSettings LoadUserSettings() => new();
    }

    private sealed class RouteMapConfigurationScope : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"catalog-route-map-{Guid.NewGuid():N}");

        public RouteMapConfigurationScope()
        {
            Directory.CreateDirectory(_directory);
            var storage = new RouteMapConfigurationStorage(Path.Combine(_directory, "route-map.json"));
            var mapper = new RouteMapConfigurationMapper(RouteMapSeed.Create());
            Manager = new RouteMapConfigurationManager(
                storage,
                mapper,
                new RouteMapConfigurationValidator(),
                new RouteMapConfigurationMigrator());
        }

        public RouteMapConfigurationManager Manager { get; }

        public void Dispose()
        {
            Manager.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
