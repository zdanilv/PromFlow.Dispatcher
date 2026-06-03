using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;
using Configurator.Infrastructure.OpcUa.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Infrastructure.OpcUa.Tests;

public sealed class OpcUaRuntimeTests
{
    [Fact]
    public async Task Runtime_StartsSelectedModes()
    {
        var client = new FakeClientService();
        var server = new FakeServerService();
        await using var runtime = CreateRuntime(client, server, CreateOptions());

        await runtime.StartAsync(OpcUaRunMode.Client);
        Assert.Equal(1, client.ConnectCount);
        Assert.Equal(1, server.StopCount);
        Assert.Equal(0, server.StartCount);

        await runtime.StartAsync(OpcUaRunMode.Server);
        Assert.Equal(1, client.DisconnectCount);
        Assert.Equal(1, server.StartCount);

        await runtime.StartAsync(OpcUaRunMode.Both);
        Assert.Equal(2, client.ConnectCount);
        Assert.Equal(2, server.StartCount);
    }

    [Fact]
    public async Task Runtime_SingleRoleCommandsDoNotTouchOtherRole()
    {
        var client = new FakeClientService();
        var server = new FakeServerService();
        await using var runtime = CreateRuntime(client, server, CreateOptions());

        await runtime.StartClientAsync();
        Assert.Equal(1, client.ConnectCount);
        Assert.Equal(0, server.StartCount);
        Assert.Equal(0, server.StopCount);

        await runtime.StopClientAsync();
        Assert.Equal(1, client.DisconnectCount);
        Assert.Equal(0, server.StartCount);
        Assert.Equal(0, server.StopCount);

        await runtime.StartServerAsync();
        Assert.Equal(1, server.StartCount);
        Assert.Equal(1, client.ConnectCount);
        Assert.Equal(1, client.DisconnectCount);

        await runtime.StopServerAsync();
        Assert.Equal(1, server.StopCount);
        Assert.Equal(1, client.ConnectCount);
        Assert.Equal(1, client.DisconnectCount);
    }

    [Fact]
    public async Task Runtime_ClientWritePublishesClientCommandSnapshot()
    {
        var client = new FakeClientService();
        var server = new FakeServerService();
        await using var runtime = CreateRuntime(client, server, CreateOptions());
        OpcUaSnapshot? snapshot = null;
        runtime.SnapshotChanged += (_, value) =>
        {
            if (value.Role == OpcUaRuntimeRole.Client)
            {
                snapshot = value;
            }
        };

        var request = new OpcUaTagWriteRequest(
            new OpcUaTagAddress("urn:test", "DemoDevice/Commands/RequestedInt", "Int32"),
            42);

        var result = await runtime.WriteClientTagAsync(request);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.NotNull(snapshot);
        Assert.Contains(snapshot.CommandValues, value => value.Address.Identifier == request.Address.Identifier && Equals(value.Value, 42));
    }

    [Fact]
    public async Task Runtime_ClientSubscriptionsUseConfiguredTelemetryTags()
    {
        var client = new FakeClientService();
        var server = new FakeServerService();
        var options = CreateOptions();
        options.Nodes.UseConfiguredTags = true;
        options.Nodes.ServerTelemetryTags =
        [
            new()
            {
                Name = "ServerOnly",
                Address = new OpcUaTagAddress("urn:test", "Device/Telemetry/ServerOnly", "Boolean"),
                Access = OpcUaTagAccess.Read
            }
        ];
        options.Nodes.ClientTelemetryTags =
        [
            new()
            {
                Name = "CustomBool",
                Address = new OpcUaTagAddress("urn:test", "Device/Telemetry/CustomBool", "Boolean"),
                Access = OpcUaTagAccess.Read
            },
            new()
            {
                Name = "CustomText",
                Address = new OpcUaTagAddress("urn:test", "Device/Telemetry/CustomText", "String"),
                Access = OpcUaTagAccess.Read
            }
        ];
        options.Nodes.ClientCommandTags =
        [
            new()
            {
                Name = "CommandOnly",
                Address = new OpcUaTagAddress("urn:test", "Device/Commands/CommandOnly", "String"),
                Access = OpcUaTagAccess.ReadWrite
            }
        ];

        await using var runtime = CreateRuntime(client, server, options);
        OpcUaSnapshot? snapshot = null;
        runtime.SnapshotChanged += (_, value) =>
        {
            if (value.Role == OpcUaRuntimeRole.Client)
            {
                snapshot = value;
            }
        };

        await runtime.StartClientAsync(options);

        Assert.Equal(
            ["Device/Telemetry/CustomBool", "Device/Telemetry/CustomText"],
            client.SubscribedIdentifiers);
        Assert.NotNull(snapshot);
        Assert.Equal(
            ["Device/Telemetry/CustomBool", "Device/Telemetry/CustomText"],
            snapshot.TelemetryValues.Select(value => value.Address.Identifier).Order().ToArray());
    }

    [Fact]
    public async Task Runtime_FailedTelemetryReadPublishesTagErrorSnapshot()
    {
        var client = new FakeClientService
        {
            ReadFailureIdentifier = "Device/Telemetry/Missing"
        };
        var server = new FakeServerService();
        var options = CreateOptions();
        options.Nodes.UseConfiguredTags = true;
        options.Nodes.ClientTelemetryTags =
        [
            new()
            {
                Name = "Missing",
                Address = new OpcUaTagAddress("urn:test", "Device/Telemetry/Missing", "String"),
                Access = OpcUaTagAccess.Read
            }
        ];

        await using var runtime = CreateRuntime(client, server, options);
        OpcUaSnapshot? snapshot = null;
        runtime.SnapshotChanged += (_, value) =>
        {
            if (value.Role == OpcUaRuntimeRole.Client)
            {
                snapshot = value;
            }
        };

        await runtime.StartClientAsync(options);

        var value = Assert.Single(snapshot!.TelemetryValues);
        Assert.Equal("Device/Telemetry/Missing", value.Address.Identifier);
        Assert.Equal("Error", value.Status);
        Assert.Contains("Read failed", value.Value?.ToString());
    }

    [Fact]
    public async Task Runtime_FailedTelemetrySubscribePublishesTagErrorSnapshot()
    {
        var client = new FakeClientService
        {
            SubscribeFailureIdentifier = "Device/Telemetry/NoSubscription"
        };
        var server = new FakeServerService();
        var options = CreateOptions();
        options.Nodes.UseConfiguredTags = true;
        options.Nodes.ClientTelemetryTags =
        [
            new()
            {
                Name = "NoSubscription",
                Address = new OpcUaTagAddress("urn:test", "Device/Telemetry/NoSubscription", "String"),
                Access = OpcUaTagAccess.Read
            }
        ];

        await using var runtime = CreateRuntime(client, server, options);
        OpcUaSnapshot? snapshot = null;
        runtime.SnapshotChanged += (_, value) =>
        {
            if (value.Role == OpcUaRuntimeRole.Client)
            {
                snapshot = value;
            }
        };

        await runtime.StartClientAsync(options);

        var value = Assert.Single(snapshot!.TelemetryValues);
        Assert.Equal("Error", value.Status);
        Assert.Contains("Subscribe failed", value.Value?.ToString());
    }

    [Fact]
    public async Task Runtime_FailedClientWritePublishesCommandErrorSnapshot()
    {
        var client = new FakeClientService
        {
            WriteShouldFail = true
        };
        var server = new FakeServerService();
        await using var runtime = CreateRuntime(client, server, CreateOptions());
        OpcUaSnapshot? snapshot = null;
        runtime.SnapshotChanged += (_, value) =>
        {
            if (value.Role == OpcUaRuntimeRole.Client)
            {
                snapshot = value;
            }
        };

        var result = await runtime.WriteClientTagAsync(new OpcUaTagWriteRequest(
            new OpcUaTagAddress("urn:test", "Device/Commands/RequestedText", "String"),
            "hello"));

        Assert.False(result.Succeeded);
        var value = Assert.Single(snapshot!.CommandValues);
        Assert.Equal("Error", value.Status);
        Assert.Contains("Write failed", value.Value?.ToString());
    }

    private static OpcUaRuntimeService CreateRuntime(
        FakeClientService client,
        FakeServerService server,
        OpcUaOptions options)
    {
        var commandState = new FakeCommandStateProvider();
        return new OpcUaRuntimeService(
            client,
            client,
            server,
            server,
            commandState,
            client,
            new TestOptionsMonitor(options),
            NullLogger<OpcUaRuntimeService>.Instance);
    }

    private static OpcUaOptions CreateOptions()
        => new()
        {
            Server =
            {
                DemoTelemetryEnabled = false
            }
        };

    private sealed class FakeClientService : IOpcUaClientService, IOpcUaTagWriter, IOpcUaConnectionStateProvider
    {
        private readonly TestObservable<OpcUaConnectionState> _states =
            new(OpcUaConnectionState.Create(OpcUaConnectionStatus.Disconnected));

        public int ConnectCount { get; private set; }
        public int DisconnectCount { get; private set; }
        public string? ReadFailureIdentifier { get; init; }
        public string? SubscribeFailureIdentifier { get; init; }
        public bool WriteShouldFail { get; init; }
        public List<string> SubscribedIdentifiers { get; } = [];

        public OpcUaConnectionState Current => _states.Current;

        public IObservable<OpcUaConnectionState> ConnectionStates => _states;

        public Task<OpcUaOperationResult> ConnectAsync(CancellationToken cancellationToken = default, OpcUaOptions? options = null)
        {
            ConnectCount++;
            _states.Publish(OpcUaConnectionState.Create(OpcUaConnectionStatus.Connected, options?.Client.EndpointUrl));
            return Task.FromResult(OpcUaOperationResult.Success());
        }

        public Task<OpcUaOperationResult> DisconnectAsync(CancellationToken cancellationToken = default)
        {
            DisconnectCount++;
            _states.Publish(OpcUaConnectionState.Create(OpcUaConnectionStatus.Disconnected));
            return Task.FromResult(OpcUaOperationResult.Success());
        }

        public Task<OpcUaOperationResult<OpcUaTagValue>> ReadTagAsync(OpcUaTagAddress address, CancellationToken cancellationToken = default)
        {
            if (address.Identifier == ReadFailureIdentifier)
            {
                return Task.FromResult(OpcUaOperationResult<OpcUaTagValue>.Failure(
                    "ReadFailed",
                    "Read failed.",
                    "Tag is unavailable."));
            }

            return Task.FromResult(OpcUaOperationResult<OpcUaTagValue>.Success(new OpcUaTagValue(address, DefaultValue(address), DateTimeOffset.Now, "Good")));
        }

        public Task<OpcUaOperationResult<IDisposable>> SubscribeAsync(
            OpcUaTagAddress address,
            Action<OpcUaTagValue> onValue,
            CancellationToken cancellationToken = default)
        {
            if (address.Identifier == SubscribeFailureIdentifier)
            {
                return Task.FromResult(OpcUaOperationResult<IDisposable>.Failure(
                    "SubscribeFailed",
                    "Subscribe failed.",
                    "Monitored item rejected."));
            }

            SubscribedIdentifiers.Add(address.Identifier);
            return Task.FromResult(OpcUaOperationResult<IDisposable>.Success(new NoopDisposable()));
        }

        public Task<OpcUaOperationResult> WriteTagAsync(OpcUaTagWriteRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(WriteShouldFail
                ? OpcUaOperationResult.Failure("WriteFailed", "Write failed.", "Server rejected value.")
                : OpcUaOperationResult.Success());

        private static object DefaultValue(OpcUaTagAddress address)
        {
            return address.DataType switch
            {
                "Boolean" => true,
                "Int32" => 1,
                _ => "value"
            };
        }
    }

    private sealed class FakeServerService : IOpcUaServerService, IOpcUaServerTagUpdater
    {
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public Task<OpcUaOperationResult> StartAsync(CancellationToken cancellationToken = default, OpcUaOptions? options = null)
        {
            StartCount++;
            return Task.FromResult(OpcUaOperationResult.Success());
        }

        public Task<OpcUaOperationResult> StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            return Task.FromResult(OpcUaOperationResult.Success());
        }

        public Task<OpcUaOperationResult> UpdateTagValueAsync(OpcUaTagValue value, CancellationToken cancellationToken = default)
            => Task.FromResult(OpcUaOperationResult.Success());
    }

    private sealed class FakeCommandStateProvider : IOpcUaServerCommandStateProvider
    {
        private readonly TestObservable<OpcUaTagValue> _commands = new();

        public OpcUaTagValue? LastCommand => null;

        public IObservable<OpcUaTagValue> CommandChanges => _commands;
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<OpcUaOptions>
    {
        public TestOptionsMonitor(OpcUaOptions currentValue)
        {
            CurrentValue = currentValue;
        }

        public OpcUaOptions CurrentValue { get; }

        public OpcUaOptions Get(string? name)
            => CurrentValue;

        public IDisposable? OnChange(Action<OpcUaOptions, string?> listener)
            => null;
    }

    private sealed class TestObservable<T> : IObservable<T>
    {
        private readonly List<IObserver<T>> _observers = [];
        private T? _current;
        private bool _hasCurrent;

        public TestObservable()
        {
        }

        public TestObservable(T current)
        {
            _current = current;
            _hasCurrent = true;
        }

        public T Current => _current!;

        public IDisposable Subscribe(IObserver<T> observer)
        {
            _observers.Add(observer);
            if (_hasCurrent)
            {
                observer.OnNext(_current!);
            }

            return new NoopDisposable();
        }

        public void Publish(T value)
        {
            _current = value;
            _hasCurrent = true;

            foreach (var observer in _observers.ToArray())
            {
                observer.OnNext(value);
            }
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
