using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Application.Services.Signals;
using Configurator.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed class AppConfigServiceTests
{
    [Fact]
    public async Task SaveSectionAsyncUpdatesOnlyRequestedSection()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"DesktopTemplate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "appsettings.json");

        try
        {
            await File.WriteAllTextAsync(
                path,
                """
                {
                  "Serilog": {
                    "MinimumLevel": {
                      "Default": "Information"
                    }
                  },
                  "Modbus": {
                    "Client": {
                      "Port": 502
                    }
                  },
                  "OpcUa": {
                    "AutostartOnWorkspaceOpen": false
                  }
                }
                """);
            var configuration = new ConfigurationBuilder().Build();
            var service = new AppConfigService(configuration, path);
            var options = new ModbusOptions
            {
                Client = new ModbusEndpointOptions { Port = 1502, UnitId = 1 },
                Server = new ModbusEndpointOptions { Port = 1502, UnitId = 1 },
                DataMap =
                [
                    new()
                    {
                        Name = "Coil0",
                        Area = ModbusDataArea.Coil,
                        Address = 0,
                        Length = 1,
                        Access = ModbusDataAccess.ReadWrite,
                        Type = ModbusValueType.Bool
                    }
                ]
            };

            await service.SaveSectionAsync(ModbusOptions.SectionName, options);

            var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
            Assert.NotNull(saved["Serilog"]);
            Assert.NotNull(saved["OpcUa"]);
            Assert.Equal(1502, saved["Modbus"]!["Client"]!["Port"]!.GetValue<int>());
            Assert.Equal("Coil", saved["Modbus"]!["DataMap"]![0]!["Area"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveSectionAsyncCreatesMissingSharedFileAndReloadsConfiguration()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"DesktopTemplate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "appsettings.json");

        try
        {
            var configuration = new ReloadCountingConfiguration();
            var service = new AppConfigService(configuration, path);
            var options = new RouteMapRuntimeOptions
            {
                SignalSource = RouteMapSignalSource.Modbus,
                StaleAfterMs = 900
            };

            await service.SaveSectionAsync(RouteMapRuntimeOptions.SectionName, options);

            Assert.True(File.Exists(path));
            Assert.Equal(1, configuration.ReloadCount);

            var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
            Assert.Equal("Modbus", saved["RouteMapRuntime"]!["SignalSource"]!.GetValue<string>());
            Assert.Equal(900, saved["RouteMapRuntime"]!["StaleAfterMs"]!.GetValue<int>());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SharedAppSettingsOverridesExeLocalRuntimeDefaults()
    {
        var configuration = BuildRuntimeConfiguration(
            new Dictionary<string, string?>
            {
                ["RouteMapRuntime:SignalSource"] = "Mock",
                ["RouteMapRuntime:StaleAfterMs"] = "1500"
            },
            new Dictionary<string, string?>
            {
                ["RouteMapRuntime:SignalSource"] = "Modbus"
            });

        var options = configuration
            .GetSection(RouteMapRuntimeOptions.SectionName)
            .Get<RouteMapRuntimeOptions>();

        Assert.NotNull(options);
        Assert.Equal(RouteMapSignalSource.Modbus, options.SignalSource);
        Assert.Equal(1500, options.StaleAfterMs);
    }

    [Fact]
    public async Task SavedRouteMapRuntimeSectionIsReadByNextUserConfiguration()
    {
        var sharedDirectory = Path.Combine(Path.GetTempPath(), $"DesktopTemplate-shared-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sharedDirectory);
        var sharedPath = Path.Combine(sharedDirectory, "appsettings.json");

        try
        {
            var baseValues = new Dictionary<string, string?>
            {
                ["RouteMapRuntime:SignalSource"] = "Mock",
                ["RouteMapRuntime:StaleAfterMs"] = "1500"
            };
            var adminConfiguration = BuildRuntimeConfiguration(baseValues, new Dictionary<string, string?>());
            var service = new AppConfigService(adminConfiguration, sharedPath);
            await service.SaveSectionAsync(
                RouteMapRuntimeOptions.SectionName,
                new RouteMapRuntimeOptions
                {
                    SignalSource = RouteMapSignalSource.Modbus,
                    StaleAfterMs = 2500
                });

            var sharedValues = ReadSharedRouteMapRuntimeValues(sharedPath);
            var userConfiguration = BuildRuntimeConfiguration(baseValues, sharedValues);
            var options = userConfiguration
                .GetSection(RouteMapRuntimeOptions.SectionName)
                .Get<RouteMapRuntimeOptions>();

            Assert.NotNull(options);
            Assert.Equal(RouteMapSignalSource.Modbus, options.SignalSource);
            Assert.Equal(2500, options.StaleAfterMs);
        }
        finally
        {
            Directory.Delete(sharedDirectory, recursive: true);
        }
    }

    [Fact]
    public void UserSettingsAreStoredAtExplicitPerUserPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"DesktopTemplate-user-settings-{Guid.NewGuid():N}");
        var appSettingsPath = Path.Combine(directory, "appsettings.json");
        var userSettingsPath = Path.Combine(directory, "profile", "user_settings.json");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["WindowSettings:Width"] = "1200",
                    ["WindowSettings:Height"] = "800",
                    ["WindowSettings:IsFullScreen"] = "false"
                })
                .Build();
            var service = new AppConfigService(configuration, appSettingsPath, userSettingsPath);
            var expected = new Configurator.Application.Services.UserSettings
            {
                Width = 1440,
                Height = 900,
                IsFullScreen = true
            };

            service.SaveUserSettings(expected);
            var actual = service.LoadUserSettings();

            Assert.True(File.Exists(userSettingsPath));
            Assert.Equal(expected.Width, actual.Width);
            Assert.Equal(expected.Height, actual.Height);
            Assert.Equal(expected.IsFullScreen, actual.IsFullScreen);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyUserSettingsAreMigratedToPerUserPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"DesktopTemplate-user-settings-migration-{Guid.NewGuid():N}");
        var appSettingsPath = Path.Combine(directory, "appsettings.json");
        var legacyPath = Path.Combine(directory, "portable", "user_settings.json");
        var userSettingsPath = Path.Combine(directory, "profile", "user_settings.json");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
            var expected = new Configurator.Application.Services.UserSettings
            {
                Width = 1366,
                Height = 768,
                IsFullScreen = true
            };
            await File.WriteAllTextAsync(
                legacyPath,
                JsonSerializer.Serialize(expected, new JsonSerializerOptions { WriteIndented = true }));

            var configuration = new ConfigurationBuilder().Build();
            var service = new AppConfigService(
                configuration,
                appSettingsPath,
                userSettingsPath,
                [legacyPath]);

            var actual = service.LoadUserSettings();

            Assert.Equal(expected.Width, actual.Width);
            Assert.Equal(expected.Height, actual.Height);
            Assert.Equal(expected.IsFullScreen, actual.IsFullScreen);
            Assert.True(File.Exists(userSettingsPath));
            Assert.True(File.Exists(legacyPath));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static IConfigurationRoot BuildRuntimeConfiguration(
        IReadOnlyDictionary<string, string?> baseValues,
        IReadOnlyDictionary<string, string?> sharedValues) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(baseValues)
            .AddInMemoryCollection(sharedValues)
            .Build();

    private static Dictionary<string, string?> ReadSharedRouteMapRuntimeValues(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var section = root[RouteMapRuntimeOptions.SectionName]!.AsObject();

        return section.ToDictionary(
            pair => $"{RouteMapRuntimeOptions.SectionName}:{pair.Key}",
            pair => pair.Value?.ToString(),
            StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ReloadCountingConfiguration : IConfigurationRoot
    {
        public int ReloadCount { get; private set; }

        public string? this[string key]
        {
            get => null;
            set { }
        }

        public IEnumerable<IConfigurationProvider> Providers => [];

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => EmptyChangeToken.Instance;

        public IConfigurationSection GetSection(string key) => new ConfigurationSection(this, key);

        public void Reload()
        {
            ReloadCount++;
        }
    }

    private sealed class EmptyChangeToken : IChangeToken
    {
        public static EmptyChangeToken Instance { get; } = new();

        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;

        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => EmptyDisposable.Instance;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static EmptyDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
