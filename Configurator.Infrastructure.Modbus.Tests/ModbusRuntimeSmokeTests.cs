using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Infrastructure.Modbus.Client;
using Configurator.Infrastructure.Modbus.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed class ModbusRuntimeSmokeTests
{
    [Fact]
    public void ModbusOptions_DefaultsDoNotAutostart()
    {
        var options = new ModbusOptions();

        Assert.False(options.AutostartOnWorkspaceOpen);
        Assert.Equal(ModbusRunMode.None, options.StartupMode);
    }

    [Fact]
    public async Task DependencyInjection_RegistersIndependentMainAndDemoStacks()
    {
        var mainPort = GetAvailablePort();
        var demoPort = GetDifferentAvailablePort(mainPort);
        var configuration = CreateConfiguration(mainPort, demoPort);
        var services = new ServiceCollection();
        AddNullLogging(services);
        services.AddSingleton<IModbusDataMapValidator, ModbusDataMapValidator>();
        services.AddModbusInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        var mainFacade = provider.GetRequiredService<IModbusTcpService>();
        var demoFacade = provider.GetRequiredService<IModbusDemoTcpService>();
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<ModbusOptions>>();
        var demoOptionsProvider = provider.GetRequiredService<IModbusDemoOptionsProvider>();

        Assert.NotSame(mainFacade, demoFacade);
        Assert.Equal(mainPort, optionsMonitor.CurrentValue.Client.Port);
        Assert.Equal(mainPort, optionsMonitor.CurrentValue.Server.Port);
        Assert.Equal(demoPort, demoOptionsProvider.CurrentValue.Client.Port);
        Assert.Equal(demoPort, demoOptionsProvider.CurrentValue.Server.Port);
        Assert.False(optionsMonitor.CurrentValue.AutostartOnWorkspaceOpen);
        Assert.False(demoOptionsProvider.CurrentValue.AutostartOnWorkspaceOpen);
        Assert.Equal(ModbusRunMode.None, optionsMonitor.CurrentValue.StartupMode);
        Assert.Equal(ModbusRunMode.None, demoOptionsProvider.CurrentValue.StartupMode);
    }

    [Fact]
    public async Task MainAndDemoServers_CanRunOnDifferentPortsIndependently()
    {
        var mainPort = GetAvailablePort();
        var demoPort = GetDifferentAvailablePort(mainPort);
        var configuration = CreateConfiguration(mainPort, demoPort);
        var services = new ServiceCollection();
        AddNullLogging(services);
        services.AddSingleton<IModbusDataMapValidator, ModbusDataMapValidator>();
        services.AddModbusInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        var mainRuntime = provider.GetRequiredService<IModbusRuntimeService>();
        var demoFacade = provider.GetRequiredService<IModbusDemoTcpService>();
        var mainOptions = provider.GetRequiredService<IOptionsMonitor<ModbusOptions>>().CurrentValue;

        await mainRuntime.StartServerAsync(mainOptions);
        var demoStart = await demoFacade.StartServerAsync();

        Assert.True(demoStart.Succeeded, demoStart.ErrorMessage);
        Assert.True(await WaitForAsync(
            () => mainRuntime.Status.ServerState == ModbusConnectionState.Running
                && demoFacade.State.ServerState == ModbusConnectionState.Running));
        Assert.Equal(ModbusConnectionState.Stopped, mainRuntime.Status.ClientState);
        Assert.Equal(ModbusConnectionState.Stopped, demoFacade.State.ClientState);

        await demoFacade.StopAsync();
        Assert.True(await WaitForAsync(() => demoFacade.State.ServerState == ModbusConnectionState.Stopped));
        Assert.Equal(ModbusConnectionState.Running, mainRuntime.Status.ServerState);

        await mainRuntime.StopAsync();
    }

    [Fact]
    public void ProgramMain_HasStaThreadAttribute()
    {
        var programPath = FindRepositoryFile("Configurator.Boot", "Program.cs");
        var source = File.ReadAllText(programPath);

        Assert.Contains("[STAThread]", source);
        Assert.Contains("StartWithClassicDesktopLifetime(args)", source);
    }

    [Fact]
    public async Task ClientWriteCycle_IsVisibleInServerSnapshot()
    {
        var port = GetAvailablePort();
        await using var server = new ModbusServerService(
            NullLogger<ModbusServerService>.Instance);
        await using var client = new ModbusClientService(
            NullLogger<ModbusClientService>.Instance);

        await server.StartAsync(new ModbusEndpointOptions
        {
            Enabled = true,
            Port = port,
            UnitId = 1,
            PollIntervalMs = 100,
            CoilCount = 10,
            RegisterCount = 20
        });

        await client.StartAsync(new ModbusEndpointOptions
        {
            Enabled = true,
            Host = "127.0.0.1",
            Port = port,
            UnitId = 1,
            PollIntervalMs = 100,
            CoilCount = 10,
            RegisterCount = 20
        });

        Assert.True(await WaitForAsync(() => client.State == ModbusConnectionState.Running));

        await client.WriteCoilAsync(2, true);
        await client.WriteRegisterAsync(3, 4321);
        await client.WriteRegistersAsync(4, ModbusRegistersCodec.EncodeString("OK", 2));

        var updated = await WaitForAsync(
            () =>
                server.Snapshot.Coils.Count > 2
                && server.Snapshot.HoldingRegisters.Count > 5
                && server.Snapshot.Coils[2]
                && server.Snapshot.HoldingRegisters[3] == 4321
                && ModbusRegistersCodec.DecodeString(server.Snapshot.HoldingRegisters, 4, 2) == "OK");

        Assert.True(updated);
    }

    [Fact]
    public async Task Client_ReadsFromConfiguredStartAddresses()
    {
        var port = GetAvailablePort();
        await using var server = new ModbusServerService(
            NullLogger<ModbusServerService>.Instance);
        await using var client = new ModbusClientService(
            NullLogger<ModbusClientService>.Instance);

        await server.StartAsync(new ModbusEndpointOptions
        {
            Enabled = true,
            Port = port,
            UnitId = 1,
            PollIntervalMs = 100,
            CoilCount = 5,
            RegisterCount = 8
        });
        await server.SetCoilAsync(2, true);
        await server.SetRegisterAsync(3, 1234);

        await client.StartAsync(new ModbusEndpointOptions
        {
            Enabled = true,
            Host = "127.0.0.1",
            Port = port,
            UnitId = 1,
            PollIntervalMs = 100,
            CoilStartAddress = 2,
            HoldingRegisterStartAddress = 2,
            CoilCount = 1,
            RegisterCount = 3
        });

        var updated = await WaitForAsync(
            () =>
                client.Snapshot.Coils.Count == 1
                && client.Snapshot.HoldingRegisters.Count == 3
                && client.Snapshot.Coils[0]
                && client.Snapshot.HoldingRegisters[1] == 1234);

        Assert.True(updated);
    }

    [Fact]
    public async Task Client_CanReadRegistersWhenCoilsAreDisabled()
    {
        var port = GetAvailablePort();
        await using var server = new ModbusServerService(
            NullLogger<ModbusServerService>.Instance);
        await using var client = new ModbusClientService(
            NullLogger<ModbusClientService>.Instance);

        await server.StartAsync(new ModbusEndpointOptions
        {
            Enabled = true,
            Port = port,
            UnitId = 1,
            PollIntervalMs = 100,
            CoilCount = 1,
            RegisterCount = 10
        });
        await server.SetRegisterAsync(4, 9876);

        await client.StartAsync(new ModbusEndpointOptions
        {
            Enabled = true,
            Host = "127.0.0.1",
            Port = port,
            UnitId = 1,
            PollIntervalMs = 100,
            CoilsEnabled = false,
            CoilCount = 10,
            RegisterCount = 10
        });

        var updated = await WaitForAsync(
            () =>
                client.Snapshot.Coils.Count == 0
                && client.Snapshot.HoldingRegisters.Count == 10
                && client.Snapshot.HoldingRegisters[4] == 9876);

        Assert.True(updated);
    }

    [Fact]
    public async Task Client_RegisterPollingContinuesWhenCoilRangeIsInvalid()
    {
        var port = GetAvailablePort();
        var lastError = string.Empty;
        await using var server = new ModbusServerService(
            NullLogger<ModbusServerService>.Instance);
        await using var client = new ModbusClientService(
            NullLogger<ModbusClientService>.Instance);
        client.StatusChanged += (_, status) => lastError = status.LastError ?? lastError;

        await server.StartAsync(new ModbusEndpointOptions
        {
            Enabled = true,
            Port = port,
            UnitId = 1,
            PollIntervalMs = 100,
            CoilCount = 1,
            RegisterCount = 10
        });
        await server.SetRegisterAsync(4, 2468);

        await client.StartAsync(new ModbusEndpointOptions
        {
            Enabled = true,
            Host = "127.0.0.1",
            Port = port,
            UnitId = 1,
            PollIntervalMs = 100,
            CoilCount = 10,
            RegisterCount = 10
        });

        var updated = await WaitForAsync(
            () =>
                client.State == ModbusConnectionState.Running
                && client.Snapshot.HoldingRegisters.Count == 10
                && client.Snapshot.HoldingRegisters[4] == 2468);

        Assert.True(updated);
        Assert.Contains("Coils: Illegal Data Address", lastError);
    }

    [Fact]
    public async Task Client_CanReadCoilsWhenRegistersAreDisabled()
    {
        var port = GetAvailablePort();
        await using var server = new ModbusServerService(
            NullLogger<ModbusServerService>.Instance);
        await using var client = new ModbusClientService(
            NullLogger<ModbusClientService>.Instance);

        await server.StartAsync(new ModbusEndpointOptions
        {
            Enabled = true,
            Port = port,
            UnitId = 1,
            PollIntervalMs = 100,
            CoilCount = 4,
            RegisterCount = 1
        });
        await server.SetCoilAsync(1, true);

        await client.StartAsync(new ModbusEndpointOptions
        {
            Enabled = true,
            Host = "127.0.0.1",
            Port = port,
            UnitId = 1,
            PollIntervalMs = 100,
            HoldingRegistersEnabled = false,
            CoilCount = 4,
            RegisterCount = 10
        });

        var updated = await WaitForAsync(
            () =>
                client.Snapshot.Coils.Count == 4
                && client.Snapshot.HoldingRegisters.Count == 0
                && client.Snapshot.Coils[1]);

        Assert.True(updated);
    }

    [Fact]
    public async Task Client_StartsRetryLoopWhenServerUnavailableAndStopCancelsIt()
    {
        var port = GetAvailablePort();
        await using var client = new ModbusClientService(
            NullLogger<ModbusClientService>.Instance);

        var options = new ModbusEndpointOptions
        {
            Enabled = true,
            Host = "127.0.0.1",
            Port = port,
            UnitId = 1,
            PollIntervalMs = 100,
            CoilCount = 10,
            RegisterCount = 20
        };

        var stopwatch = Stopwatch.StartNew();
        await client.StartAsync(options);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(500));
        Assert.True(await WaitForAsync(() => client.State == ModbusConnectionState.Reconnecting));

        await client.StopAsync();
        Assert.Equal(ModbusConnectionState.Stopped, client.State);

        await Task.Delay(options.PollIntervalMs * 2);
        Assert.Equal(ModbusConnectionState.Stopped, client.State);
    }

    [Fact]
    public async Task Runtime_StartsSelectedModes()
    {
        var client = new FakeClientService();
        var server = new FakeServerService();
        var options = new ModbusOptions();
        await using var runtime = new ModbusRuntimeService(
            client,
            server,
            new TestOptionsMonitor(options),
            NullLogger<ModbusRuntimeService>.Instance);

        await runtime.StartAsync(ModbusRunMode.Client);
        Assert.Equal(1, client.StartCount);
        Assert.Equal(1, server.StopCount);
        Assert.Equal(0, server.StartCount);

        await runtime.StartAsync(ModbusRunMode.Server);
        Assert.Equal(1, client.StopCount);
        Assert.Equal(1, server.StartCount);

        await runtime.StartAsync(ModbusRunMode.Both);
        Assert.Equal(2, client.StartCount);
        Assert.Equal(2, server.StartCount);
    }

    [Fact]
    public async Task Runtime_SingleRoleCommandsDoNotTouchOtherRole()
    {
        var client = new FakeClientService();
        var server = new FakeServerService();
        var options = new ModbusOptions();
        await using var runtime = new ModbusRuntimeService(
            client,
            server,
            new TestOptionsMonitor(options),
            NullLogger<ModbusRuntimeService>.Instance);

        await runtime.StartClientAsync();
        Assert.Equal(1, client.StartCount);
        Assert.Equal(0, client.StopCount);
        Assert.Equal(0, server.StartCount);
        Assert.Equal(0, server.StopCount);

        await runtime.StopClientAsync();
        Assert.Equal(1, client.StartCount);
        Assert.Equal(1, client.StopCount);
        Assert.Equal(0, server.StartCount);
        Assert.Equal(0, server.StopCount);

        await runtime.StartServerAsync();
        Assert.Equal(1, client.StartCount);
        Assert.Equal(1, client.StopCount);
        Assert.Equal(1, server.StartCount);
        Assert.Equal(0, server.StopCount);

        await runtime.StopServerAsync();
        Assert.Equal(1, client.StartCount);
        Assert.Equal(1, client.StopCount);
        Assert.Equal(1, server.StartCount);
        Assert.Equal(1, server.StopCount);
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        var startedAt = DateTimeOffset.UtcNow;

        while (DateTimeOffset.UtcNow - startedAt < TimeSpan.FromSeconds(5))
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static int GetDifferentAvailablePort(int otherPort)
    {
        int port;

        do
        {
            port = GetAvailablePort();
        }
        while (port == otherPort);

        return port;
    }

    private static IConfiguration CreateConfiguration(int mainPort, int demoPort)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Modbus:AutostartOnWorkspaceOpen"] = "false",
                ["Modbus:StartupMode"] = "None",
                ["Modbus:Client:Host"] = "127.0.0.1",
                ["Modbus:Client:Port"] = mainPort.ToString(CultureInfo.InvariantCulture),
                ["Modbus:Client:UnitId"] = "1",
                ["Modbus:Client:PollIntervalMs"] = "100",
                ["Modbus:Client:CoilCount"] = "4",
                ["Modbus:Client:RegisterCount"] = "4",
                ["Modbus:Server:Enabled"] = "true",
                ["Modbus:Server:BindAddress"] = "127.0.0.1",
                ["Modbus:Server:Port"] = mainPort.ToString(CultureInfo.InvariantCulture),
                ["Modbus:Server:UnitId"] = "1",
                ["Modbus:Server:PollIntervalMs"] = "100",
                ["Modbus:Server:CoilCount"] = "4",
                ["Modbus:Server:RegisterCount"] = "4",
                ["ModbusDemo:AutostartOnWorkspaceOpen"] = "false",
                ["ModbusDemo:StartupMode"] = "None",
                ["ModbusDemo:Client:Host"] = "127.0.0.1",
                ["ModbusDemo:Client:Port"] = demoPort.ToString(CultureInfo.InvariantCulture),
                ["ModbusDemo:Client:UnitId"] = "1",
                ["ModbusDemo:Client:PollIntervalMs"] = "100",
                ["ModbusDemo:Client:CoilCount"] = "4",
                ["ModbusDemo:Client:RegisterCount"] = "4",
                ["ModbusDemo:Server:Enabled"] = "true",
                ["ModbusDemo:Server:BindAddress"] = "127.0.0.1",
                ["ModbusDemo:Server:Port"] = demoPort.ToString(CultureInfo.InvariantCulture),
                ["ModbusDemo:Server:UnitId"] = "1",
                ["ModbusDemo:Server:PollIntervalMs"] = "100",
                ["ModbusDemo:Server:CoilCount"] = "4",
                ["ModbusDemo:Server:RegisterCount"] = "4"
            })
            .Build();

    private static void AddNullLogging(IServiceCollection services)
    {
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    }

    private static string FindRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());

            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Не найден файл репозитория.", Path.Combine(relativeParts));
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<ModbusOptions>
    {
        public TestOptionsMonitor(ModbusOptions currentValue)
        {
            CurrentValue = currentValue;
        }

        public ModbusOptions CurrentValue { get; }

        public ModbusOptions Get(string? name)
        {
            return CurrentValue;
        }

        public IDisposable? OnChange(Action<ModbusOptions, string?> listener)
        {
            return null;
        }
    }

    private sealed class FakeClientService : IModbusClientService
    {
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public ModbusConnectionState State { get; private set; } = ModbusConnectionState.Stopped;
        public ModbusSnapshot Snapshot { get; private set; } = ModbusSnapshot.Empty;
        public event EventHandler<ModbusStatus>? StatusChanged;
        public event EventHandler<ModbusSnapshot>? SnapshotChanged;

        public Task StartAsync(ModbusEndpointOptions options, CancellationToken cancellationToken = default)
        {
            StartCount++;
            State = ModbusConnectionState.Running;
            Snapshot = new ModbusSnapshot { Role = ModbusRuntimeRole.Client };
            StatusChanged?.Invoke(this, new ModbusStatus { ClientState = State });
            SnapshotChanged?.Invoke(this, Snapshot);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            State = ModbusConnectionState.Stopped;
            StatusChanged?.Invoke(this, new ModbusStatus { ClientState = State });
            return Task.CompletedTask;
        }

        public Task WriteCoilAsync(int address, bool value, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task WriteRegisterAsync(int address, ushort value, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task WriteRegistersAsync(int startAddress, ushort[] values, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeServerService : IModbusServerService
    {
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public ModbusConnectionState State { get; private set; } = ModbusConnectionState.Stopped;
        public ModbusSnapshot Snapshot { get; private set; } = ModbusSnapshot.Empty;
        public event EventHandler<ModbusStatus>? StatusChanged;
        public event EventHandler<ModbusSnapshot>? SnapshotChanged;

        public Task StartAsync(ModbusEndpointOptions options, CancellationToken cancellationToken = default)
        {
            StartCount++;
            State = ModbusConnectionState.Running;
            Snapshot = new ModbusSnapshot { Role = ModbusRuntimeRole.Server };
            StatusChanged?.Invoke(this, new ModbusStatus { ServerState = State });
            SnapshotChanged?.Invoke(this, Snapshot);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            State = ModbusConnectionState.Stopped;
            StatusChanged?.Invoke(this, new ModbusStatus { ServerState = State });
            return Task.CompletedTask;
        }

        public Task SetCoilAsync(int address, bool value, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task SetRegisterAsync(int address, ushort value, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task SetRegistersAsync(int startAddress, ushort[] values, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

}
