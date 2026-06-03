using System.Net;
using System.Net.Sockets;
using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;
using Configurator.Infrastructure.OpcUa.Client;
using Configurator.Infrastructure.OpcUa.Runtime;
using Configurator.Infrastructure.OpcUa.Server;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Infrastructure.OpcUa.Tests;

public sealed class OpcUaIntegrationTests
{
    [Fact]
    public async Task ClientAndServer_ShouldReadSubscribeAndWriteDemoTags()
    {
        var options = CreateOptions(FindFreeTcpPort());
        var monitor = new TestOptionsMonitor(options);
        var configurationFactory = new OpcUaApplicationConfigurationFactory();
        var validator = new OpcUaTagWriteRequestValidator();
        var securityProvider = new DisabledOpcUaSecurityProvider();
        var identityProvider = new AnonymousOpcUaIdentityProvider();
        var commandState = new OpcUaServerCommandState(NullLogger<OpcUaServerCommandState>.Instance);
        await using var server = new OpcUaServerService(
            monitor,
            configurationFactory,
            commandState,
            NullLogger<OpcUaServerService>.Instance);
        await using var client = new OpcUaClientService(
            monitor,
            configurationFactory,
            securityProvider,
            identityProvider,
            validator,
            NullLogger<OpcUaClientService>.Instance);
        var browser = new OpcUaTagBrowserService(
            monitor,
            configurationFactory,
            securityProvider,
            identityProvider,
            NullLogger<OpcUaTagBrowserService>.Instance);

        try
        {
            var serverStart = await server.StartAsync(CancellationToken.None, options);
            Assert.True(serverStart.Succeeded, serverStart.Error?.Message);

            var connect = await ConnectWithRetryAsync(client, options, TimeSpan.FromSeconds(10));
            Assert.True(connect.Succeeded, $"{connect.Error?.Message} {connect.Error?.Details}");

            var browse = await browser.BrowseAsync(new OpcUaBrowseRequest
            {
                EndpointUrl = options.Client.EndpointUrl,
                Target = OpcUaImportTarget.ServerTelemetry,
                Options = options
            });
            Assert.True(browse.Succeeded, browse.Error?.Message);
            Assert.Contains(
                Flatten(browse.Value!.Nodes),
                node => node.Name == "IsRunning"
                        && node.IsSelectable
                        && node.Address?.NamespaceUri == options.Nodes.NamespaceUri);

            var read = await client.ReadTagAsync(options.Nodes.IsRunningAddress(), CancellationToken.None);
            Assert.True(read.Succeeded, read.Error?.Message);
            Assert.IsType<bool>(read.Value?.Value);

            var notification = new TaskCompletionSource<OpcUaTagValue>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var subscription = await client.SubscribeAsync(
                options.Nodes.IsRunningAddress(),
                value =>
                {
                    if (value.Value is false)
                    {
                        notification.TrySetResult(value);
                    }
                },
                CancellationToken.None);
            Assert.True(subscription.Succeeded, subscription.Error?.Message);
            using var subscriptionHandle = subscription.Value;

            var isRunningUpdate = await server.UpdateTagValueAsync(
                new OpcUaTagValue(options.Nodes.IsRunningAddress(), false, DateTimeOffset.Now, "Good"),
                CancellationToken.None);
            Assert.True(isRunningUpdate.Succeeded, isRunningUpdate.Error?.Message);

            var notified = await notification.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(Assert.IsType<bool>(notified.Value));

            var command = new TaskCompletionSource<OpcUaTagValue>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var commandSubscription = commandState.CommandChanges.Subscribe(
                new ActionObserver<OpcUaTagValue>(value =>
                {
                    if (value.Address.Identifier == options.Nodes.RequestedIntIdentifier)
                    {
                        command.TrySetResult(value);
                    }
                }));

            var write = await client.WriteTagAsync(
                new OpcUaTagWriteRequest(options.Nodes.RequestedIntAddress(), 77),
                CancellationToken.None);
            Assert.True(write.Succeeded, write.Error?.Message);

            var serverCommand = await command.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(options.Nodes.RequestedIntIdentifier, serverCommand.Address.Identifier);
            Assert.Equal(77, Assert.IsType<int>(serverCommand.Value));
            Assert.Equal(serverCommand, commandState.LastCommand);
        }
        finally
        {
            await client.DisconnectAsync(CancellationToken.None);
            await server.StopAsync(CancellationToken.None);
        }
    }

    private static async Task<OpcUaOperationResult> ConnectWithRetryAsync(
        IOpcUaClientService client,
        OpcUaOptions options,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.Now.Add(timeout);
        OpcUaOperationResult lastResult = OpcUaOperationResult.Failure(
            "ConnectNotStarted",
            "Connect has not been attempted.");

        while (DateTimeOffset.Now < deadline)
        {
            lastResult = await client.ConnectAsync(CancellationToken.None, options);
            if (lastResult.Succeeded)
            {
                return lastResult;
            }

            await Task.Delay(250);
        }

        return lastResult;
    }

    private static OpcUaOptions CreateOptions(int port)
    {
        var endpointUrl = $"opc.tcp://localhost:{port}";

        return new OpcUaOptions
        {
            Client =
            {
                EndpointUrl = endpointUrl,
                ConnectTimeoutMilliseconds = 30000,
                ReconnectPeriodMilliseconds = 500,
                SubscriptionPublishingIntervalMilliseconds = 250,
                CommandWriteIntervalMilliseconds = 250
            },
            Server =
            {
                EndpointUrl = endpointUrl,
                DemoTelemetryEnabled = false,
                TelemetryUpdateIntervalMilliseconds = 1000
            },
            Security =
            {
                Mode = "None",
                Authentication =
                {
                    Mode = "Anonymous"
                },
                Certificates =
                {
                    Enabled = false
                }
            }
        };
    }

    private static int FindFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static IEnumerable<OpcUaBrowseNode> Flatten(IEnumerable<OpcUaBrowseNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
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

    private sealed class ActionObserver<T> : IObserver<T>
    {
        private readonly Action<T> _onNext;

        public ActionObserver(Action<T> onNext)
        {
            _onNext = onNext;
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value)
            => _onNext(value);
    }
}
