using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Configurator.Application.Services;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Profiles;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Desktop.Workspace.ModbusProfile;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed class ModbusTcpProfileTransferServiceTests
{
    [Fact]
    public async Task ExportAndRead_ProfileRoundTrip_ExcludesLegacyDemoDataMap()
    {
        var runtime = CreateRuntimeOptions();
        runtime.DataMap = [new ModbusDataPointOptions { Name = "LegacyDemo", Area = ModbusDataArea.Coil, Type = ModbusValueType.Bool }];
        var map = new ModbusOptions
        {
            DataMap = [new ModbusDataPointOptions { Name = "system.fault", Area = ModbusDataArea.Coil, Type = ModbusValueType.Bool }],
            AlarmMap = [CreateAlarm()],
            AddressLabels = [new ModbusAddressLabelOptions { Area = ModbusDataArea.HoldingRegister, Address = 3, BitIndex = 1, DisplayName = "Пуск" }]
        };
        var service = CreateService(runtime, map, out var config, out _);
        var path = CreateTemporaryPath();

        try
        {
            await service.ExportAsync(path);
            var json = await File.ReadAllTextAsync(path);
            var result = await service.ReadAndValidateAsync(path);

            Assert.True(result.Succeeded);
            Assert.NotNull(result.Profile);
            Assert.Single(result.Profile!.DataMap);
            Assert.Single(result.Profile.AlarmMap);
            var label = Assert.Single(result.Profile.AddressLabels);
            Assert.Equal((ModbusDataArea.HoldingRegister, 3, 1, "Пуск"), (label.Area, label.Address, label.BitIndex, label.DisplayName));
            Assert.DoesNotContain("LegacyDemo", json, StringComparison.Ordinal);
            Assert.Equal(0, config.SaveSectionsCallCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadAndValidate_LegacyProfileWithoutAddressLabels_UsesEmptyCatalog()
    {
        var service = CreateService(CreateRuntimeOptions(), new ModbusOptions(), out _, out _);
        var profile = ModbusTcpProfile.Create(CreateRuntimeOptions(), new ModbusOptions());
        var path = CreateTemporaryPath();

        try
        {
            var node = JsonNode.Parse(JsonSerializer.Serialize(profile, JsonOptions))!.AsObject();
            node.Remove("AddressLabels");
            await File.WriteAllTextAsync(path, node.ToJsonString(JsonOptions));

            var result = await service.ReadAndValidateAsync(path);

            Assert.True(result.Succeeded);
            Assert.Empty(result.Profile!.AddressLabels);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadAndValidate_DuplicateAddressLabels_Fails()
    {
        var service = CreateService(CreateRuntimeOptions(), new ModbusOptions(), out _, out _);
        var profile = ModbusTcpProfile.Create(CreateRuntimeOptions(), new ModbusOptions());
        profile.AddressLabels =
        [
            new ModbusAddressLabelOptions { Area = ModbusDataArea.Coil, Address = 2, DisplayName = "Первый" },
            new ModbusAddressLabelOptions { Area = ModbusDataArea.Coil, Address = 2, DisplayName = "Второй" }
        ];
        var path = CreateTemporaryPath();

        try
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(profile, JsonOptions));

            var result = await service.ReadAndValidateAsync(path);

            Assert.False(result.Succeeded);
            Assert.Contains("уже задана", result.ErrorMessage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadAndValidate_InvalidAddressLabelBit_Fails()
    {
        var service = CreateService(CreateRuntimeOptions(), new ModbusOptions(), out _, out _);
        var profile = ModbusTcpProfile.Create(CreateRuntimeOptions(), new ModbusOptions());
        profile.AddressLabels =
        [
            new ModbusAddressLabelOptions
            {
                Area = ModbusDataArea.HoldingRegister,
                Address = 0,
                BitIndex = 16,
                DisplayName = "Некорректный бит"
            }
        ];
        var path = CreateTemporaryPath();

        try
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(profile, JsonOptions));

            var result = await service.ReadAndValidateAsync(path);

            Assert.False(result.Succeeded);
            Assert.Contains("0..15", result.ErrorMessage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadAndValidate_InvalidOrUnsupportedProfile_DoesNotSaveConfiguration()
    {
        var service = CreateService(CreateRuntimeOptions(), new ModbusOptions(), out var config, out _);
        var malformed = CreateTemporaryPath();
        var unsupported = CreateTemporaryPath();

        try
        {
            await File.WriteAllTextAsync(malformed, "{ not json }");
            var malformedResult = await service.ReadAndValidateAsync(malformed);

            var profile = ModbusTcpProfile.Create(CreateRuntimeOptions(), new ModbusOptions());
            profile.Version = ModbusTcpProfile.CurrentVersion + 1;
            await File.WriteAllTextAsync(unsupported, JsonSerializer.Serialize(profile, JsonOptions));
            var unsupportedResult = await service.ReadAndValidateAsync(unsupported);

            Assert.False(malformedResult.Succeeded);
            Assert.False(unsupportedResult.Succeeded);
            Assert.Equal(0, config.SaveSectionsCallCount);
        }
        finally
        {
            File.Delete(malformed);
            File.Delete(unsupported);
        }
    }

    [Fact]
    public async Task ReadAndApply_ProfileOutsideRanges_ProposesAndPersistsMinimumRanges()
    {
        var runtime = CreateRuntimeOptions(coilCount: 1, registerCount: 1);
        runtime.DataMap = [new ModbusDataPointOptions { Name = "LegacyDemo", Area = ModbusDataArea.Coil, Type = ModbusValueType.Bool }];
        var map = new ModbusOptions();
        var service = CreateService(runtime, map, out var config, out var dataMapRuntime);
        var profile = ModbusTcpProfile.Create(runtime, new ModbusOptions
        {
            DataMap =
            [
                new ModbusDataPointOptions
                {
                    Name = "two.registers",
                    Area = ModbusDataArea.HoldingRegister,
                    Address = 5,
                    Length = 2,
                    Access = ModbusDataAccess.Read,
                    Type = ModbusValueType.Real
                },
                new ModbusDataPointOptions
                {
                    Name = "coil.signal",
                    Area = ModbusDataArea.Coil,
                    Address = 8,
                    Length = 1,
                    Access = ModbusDataAccess.Read,
                    Type = ModbusValueType.Bool
                }
            ],
            AlarmMap =
            [
                new ModbusAlarmOptions
                {
                    Id = "alarm.range",
                    Message = "Проверка",
                    RegisterValueEnabled = true,
                    RegisterValuePrefix = "Значение: ",
                    RegisterValueAddress = 9,
                    Alarm = new ModbusBitAddressOptions { Area = ModbusDataArea.Coil, Address = 0 },
                    Acknowledgement = new ModbusBitAddressOptions { Area = ModbusDataArea.Coil, Address = 10 }
                }
            ]
        });
        profile.AddressLabels =
        [
            new ModbusAddressLabelOptions { Area = ModbusDataArea.Coil, Address = 0, DisplayName = "Вход" }
        ];
        var path = CreateTemporaryPath();

        try
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(profile, JsonOptions));
            var preview = await service.ReadAndValidateAsync(path);

            Assert.True(preview.Succeeded);
            Assert.True(preview.RangeCorrection.HasChanges);
            Assert.Equal(11, preview.RangeCorrection.ClientCoilCount);
            Assert.Equal(10, preview.RangeCorrection.ClientRegisterCount);

            var applied = await service.ApplyAsync(preview.Profile!, applyRangeCorrection: true);

            Assert.True(applied.Succeeded);
            Assert.Equal(1, config.SaveSectionsCallCount);
            Assert.Contains(ModbusOptions.SectionName, config.LastSections.Keys);
            Assert.Contains(ModbusOptions.DemoSectionName, config.LastSections.Keys);
            var savedRuntime = Assert.IsType<ModbusOptions>(config.LastSections[ModbusOptions.DemoSectionName]);
            Assert.Equal(11, savedRuntime.Client.CoilCount);
            Assert.Equal(10, savedRuntime.Client.RegisterCount);
            Assert.Single(savedRuntime.DataMap); // legacy demo-map is retained, not imported

            var savedMap = Assert.IsType<ModbusOptions>(config.LastSections[ModbusOptions.SectionName]);
            Assert.Equal(11, savedMap.Client.CoilCount);
            Assert.Equal(10, savedMap.Client.RegisterCount);
            Assert.Equal("Вход", Assert.Single(savedMap.AddressLabels).DisplayName);
            Assert.Equal(2, dataMapRuntime.LastDataMap.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ModbusTcpProfileTransferService CreateService(
        ModbusOptions runtime,
        ModbusOptions map,
        out RecordingAppConfigService config,
        out RecordingDataMapRuntime dataMapRuntime)
    {
        config = new RecordingAppConfigService();
        dataMapRuntime = new RecordingDataMapRuntime();
        return new ModbusTcpProfileTransferService(
            config,
            new StaticOptionsMonitor(map),
            new StaticDemoOptionsProvider(runtime),
            new ModbusDataMapValidator(),
            new ModbusAlarmMapValidator(),
            dataMapRuntime);
    }

    private static ModbusOptions CreateRuntimeOptions(int coilCount = 20, int registerCount = 20)
        => new()
        {
            AutostartOnWorkspaceOpen = true,
            StartupMode = ModbusRunMode.Both,
            WriteConfirmationTimeoutMs = 2000,
            Client = new ModbusEndpointOptions
            {
                Host = "127.0.0.1",
                Port = 1502,
                UnitId = 1,
                PollIntervalMs = 500,
                CoilsEnabled = true,
                HoldingRegistersEnabled = true,
                CoilCount = coilCount,
                RegisterCount = registerCount
            },
            Server = new ModbusEndpointOptions
            {
                BindAddress = "127.0.0.1",
                Port = 1502,
                UnitId = 1,
                PollIntervalMs = 500,
                CoilsEnabled = true,
                HoldingRegistersEnabled = true,
                CoilCount = coilCount,
                RegisterCount = registerCount
            }
        };

    private static ModbusAlarmOptions CreateAlarm(int acknowledgementAddress = 1)
        => new()
        {
            Id = "alarm.test",
            Message = "Проверка",
            Alarm = new ModbusBitAddressOptions { Area = ModbusDataArea.Coil, Address = 0 },
            Acknowledgement = new ModbusBitAddressOptions { Area = ModbusDataArea.Coil, Address = acknowledgementAddress }
        };

    private static string CreateTemporaryPath()
        => Path.Combine(Path.GetTempPath(), $"modbus-profile-{Guid.NewGuid():N}.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed class StaticDemoOptionsProvider(ModbusOptions options) : IModbusDemoOptionsProvider
    {
        public ModbusOptions CurrentValue => options.Clone();
    }

    private sealed class StaticOptionsMonitor(ModbusOptions options) : IOptionsMonitor<ModbusOptions>
    {
        public ModbusOptions CurrentValue => options.Clone();
        public ModbusOptions Get(string? name) => options.Clone();
        public IDisposable OnChange(Action<ModbusOptions, string?> listener) => EmptyDisposable.Instance;
    }

    private sealed class RecordingDataMapRuntime : IModbusDataMapRuntime
    {
        public IReadOnlyList<ModbusDataPointOptions> LastDataMap { get; private set; } = [];

        public ModbusOperationResult ApplyDataMap(IReadOnlyList<ModbusDataPointOptions> dataMap)
        {
            LastDataMap = dataMap.Select(point => point.Clone()).ToList();
            return ModbusOperationResult.Success();
        }
    }

    private sealed class RecordingAppConfigService : IAppConfigService
    {
        public int SaveSectionsCallCount { get; private set; }
        public IReadOnlyDictionary<string, object> LastSections { get; private set; } = new Dictionary<string, object>();

        public T GetSection<T>(string sectionName) where T : class, new() => new();
        public string GetValue(string key) => string.Empty;
        public Task SaveSectionAsync<T>(string sectionName, T value, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveSectionsAsync(IReadOnlyDictionary<string, object> sections, CancellationToken ct = default)
        {
            SaveSectionsCallCount++;
            LastSections = sections.ToDictionary(pair => pair.Key, pair => pair.Value);
            return Task.CompletedTask;
        }
        public void SaveUserSettings(UserSettings settings) { }
        public UserSettings LoadUserSettings() => new();
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static EmptyDisposable Instance { get; } = new();
        public void Dispose() { }
    }
}
