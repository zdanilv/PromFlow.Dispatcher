using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
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
}
