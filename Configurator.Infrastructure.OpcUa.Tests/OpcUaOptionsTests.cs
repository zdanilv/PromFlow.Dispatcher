using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Configurator.Infrastructure.OpcUa.Tests;

public sealed class OpcUaOptionsTests
{
    [Fact]
    public void Defaults_ShouldNotAutostartAndUseAnonymousNoCertificateSecurity()
    {
        var options = new OpcUaOptions();

        Assert.False(options.AutostartOnWorkspaceOpen);
        Assert.Equal(OpcUaRunMode.None, options.StartupMode);
        Assert.Equal("None", options.Security.Mode);
        Assert.Equal("Anonymous", options.Security.Authentication.Mode);
        Assert.False(options.Security.Certificates.Enabled);
    }

    [Fact]
    public void JsonOptions_ShouldBindDesktopRuntimeFields()
    {
        var values = new Dictionary<string, string?>
        {
            ["OpcUa:AutostartOnWorkspaceOpen"] = "true",
            ["OpcUa:StartupMode"] = "Both",
            ["OpcUa:Client:Enabled"] = "false",
            ["OpcUa:Client:EndpointUrl"] = "opc.tcp://localhost:6001",
            ["OpcUa:Server:Enabled"] = "true",
            ["OpcUa:Server:DemoTelemetryEnabled"] = "false",
            ["OpcUa:Server:EndpointUrl"] = "opc.tcp://localhost:6002",
            ["OpcUa:Server:TelemetryUpdateIntervalMilliseconds"] = "3000",
            ["OpcUa:Nodes:NamespaceUri"] = "urn:test"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var options = configuration
            .GetSection(OpcUaOptions.SectionName)
            .Get<OpcUaOptions>();

        Assert.NotNull(options);
        Assert.True(options.AutostartOnWorkspaceOpen);
        Assert.Equal(OpcUaRunMode.Both, options.StartupMode);
        Assert.False(options.Client.Enabled);
        Assert.Equal("opc.tcp://localhost:6001", options.Client.EndpointUrl);
        Assert.True(options.Server.Enabled);
        Assert.False(options.Server.DemoTelemetryEnabled);
        Assert.Equal("opc.tcp://localhost:6002", options.Server.EndpointUrl);
        Assert.Equal(3000, options.Server.TelemetryUpdateIntervalMilliseconds);
        Assert.Equal("urn:test", options.Nodes.NamespaceUri);
    }

    [Fact]
    public void JsonOptions_ShouldBindDynamicTagLists()
    {
        var values = new Dictionary<string, string?>
        {
            ["OpcUa:Nodes:NamespaceUri"] = "urn:test",
            ["OpcUa:Nodes:UseConfiguredTags"] = "true",
            ["OpcUa:Nodes:TelemetryTags:0:Name"] = "Temperature",
            ["OpcUa:Nodes:TelemetryTags:0:Address:NamespaceUri"] = "urn:test",
            ["OpcUa:Nodes:TelemetryTags:0:Address:Identifier"] = "Device/Telemetry/Temperature",
            ["OpcUa:Nodes:TelemetryTags:0:Address:DataType"] = "Int32",
            ["OpcUa:Nodes:TelemetryTags:0:Access"] = "Read",
            ["OpcUa:Nodes:CommandTags:0:Name"] = "RequestedText",
            ["OpcUa:Nodes:CommandTags:0:Address:NamespaceUri"] = "urn:test",
            ["OpcUa:Nodes:CommandTags:0:Address:Identifier"] = "Device/Commands/RequestedText",
            ["OpcUa:Nodes:CommandTags:0:Address:DataType"] = "String",
            ["OpcUa:Nodes:CommandTags:0:Access"] = "ReadWrite"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var options = configuration
            .GetSection(OpcUaOptions.SectionName)
            .Get<OpcUaOptions>();

        Assert.NotNull(options);
        Assert.True(options.Nodes.UseConfiguredTags);
        var telemetry = Assert.Single(options.Nodes.TelemetryDefinitions());
        Assert.Equal("Temperature", telemetry.Name);
        Assert.Equal("Device/Telemetry/Temperature", telemetry.Address.Identifier);
        Assert.Equal("Int32", telemetry.Address.DataType);
        Assert.Equal(OpcUaTagAccess.Read, telemetry.Access);

        var command = Assert.Single(options.Nodes.CommandDefinitions());
        Assert.Equal("RequestedText", command.Name);
        Assert.Equal("Device/Commands/RequestedText", command.Address.Identifier);
        Assert.Equal(OpcUaTagAccess.ReadWrite, command.Access);
    }

    [Fact]
    public void NodeOptions_ShouldSplitServerAndClientTagListsAndMigrateLegacyLists()
    {
        var nodes = new OpcUaNodeOptions
        {
            UseConfiguredTags = true,
            TelemetryTags =
            [
                new()
                {
                    Name = "LegacyTelemetry",
                    Address = new OpcUaTagAddress("urn:test", "Legacy/Telemetry", "String"),
                    Access = OpcUaTagAccess.Read
                }
            ],
            CommandTags =
            [
                new()
                {
                    Name = "LegacyCommand",
                    Address = new OpcUaTagAddress("urn:test", "Legacy/Command", "String"),
                    Access = OpcUaTagAccess.ReadWrite
                }
            ]
        };

        Assert.Equal("Legacy/Telemetry", Assert.Single(nodes.ServerTelemetryDefinitions()).Address.Identifier);
        Assert.Equal("Legacy/Telemetry", Assert.Single(nodes.ClientTelemetryDefinitions()).Address.Identifier);
        Assert.Equal("Legacy/Command", Assert.Single(nodes.ServerCommandDefinitions()).Address.Identifier);
        Assert.Equal("Legacy/Command", Assert.Single(nodes.ClientCommandDefinitions()).Address.Identifier);

        nodes.ServerTelemetryTags =
        [
            new()
            {
                Name = "ServerOnly",
                Address = new OpcUaTagAddress("urn:test", "Server/Telemetry", "String"),
                Access = OpcUaTagAccess.Read
            }
        ];
        nodes.ClientTelemetryTags =
        [
            new()
            {
                Name = "ClientOnly",
                Address = new OpcUaTagAddress("urn:test", "Client/Telemetry", "String"),
                Access = OpcUaTagAccess.Read
            }
        ];

        Assert.Equal("Server/Telemetry", Assert.Single(nodes.ServerTelemetryDefinitions()).Address.Identifier);
        Assert.Equal("Client/Telemetry", Assert.Single(nodes.ClientTelemetryDefinitions()).Address.Identifier);
    }

    [Fact]
    public void Clone_ShouldDeepCopyKnownEndpoints()
    {
        var options = new OpcUaOptions
        {
            KnownEndpoints =
            [
                new()
                {
                    Name = "Server A",
                    EndpointUrl = "opc.tcp://localhost:4841",
                    Source = "Test"
                }
            ]
        };

        var clone = options.Clone();
        clone.KnownEndpoints[0].EndpointUrl = "opc.tcp://localhost:4842";

        Assert.Equal("opc.tcp://localhost:4841", options.KnownEndpoints[0].EndpointUrl);
    }

    [Fact]
    public void NodeOptions_ShouldUseLegacyDemoTagsUntilConfiguredListsAreEnabled()
    {
        var nodes = new OpcUaNodeOptions
        {
            NamespaceUri = "urn:legacy",
            IsRunningIdentifier = "Legacy/IsRunning",
            CounterIdentifier = "Legacy/Counter",
            ServerMessageIdentifier = "Legacy/Message",
            RequestedBoolIdentifier = "Legacy/Bool",
            RequestedIntIdentifier = "Legacy/Int",
            RequestedTextIdentifier = "Legacy/Text"
        };

        Assert.Equal(
            ["Legacy/IsRunning", "Legacy/Counter", "Legacy/Message"],
            nodes.TelemetryAddresses().Select(address => address.Identifier).ToArray());
        Assert.Equal(
            ["Legacy/Bool", "Legacy/Int", "Legacy/Text"],
            nodes.CommandAddresses().Select(address => address.Identifier).ToArray());
    }

    [Fact]
    public void Clone_ShouldDeepCopyDynamicTags()
    {
        var options = new OpcUaOptions
        {
            Nodes =
            {
                UseConfiguredTags = true,
                TelemetryTags =
                [
                    new()
                    {
                        Name = "T1",
                        Address = new OpcUaTagAddress("urn:test", "Device/T1", "Boolean"),
                        Access = OpcUaTagAccess.Read
                    }
                ]
            }
        };

        var clone = options.Clone();
        clone.Nodes.TelemetryTags[0].Name = "Changed";
        clone.Nodes.TelemetryTags[0].Address.Identifier = "Device/Changed";

        Assert.Equal("T1", options.Nodes.TelemetryTags[0].Name);
        Assert.Equal("Device/T1", options.Nodes.TelemetryTags[0].Address.Identifier);
    }
}
