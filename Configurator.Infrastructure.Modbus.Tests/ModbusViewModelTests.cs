using Configurator.Application.Services;
using Configurator.Application.Services.Dialogs;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;
using Configurator.Desktop.Workspace.Modbus;
using Microsoft.Extensions.Options;
using ReactiveUI.Builder;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed class ModbusViewModelTests
{
    static ModbusViewModelTests()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();
    }

    public static TheoryData<ModbusConnectionState, ModbusConnectionState, bool, bool, bool, bool, bool, bool> ButtonStates
        => new()
        {
            { ModbusConnectionState.Stopped, ModbusConnectionState.Stopped, true, true, false, false, false, true },
            { ModbusConnectionState.Running, ModbusConnectionState.Stopped, false, true, true, false, true, false },
            { ModbusConnectionState.Stopped, ModbusConnectionState.Running, true, false, false, true, true, false },
            { ModbusConnectionState.Running, ModbusConnectionState.Running, false, false, true, true, true, false },
            { ModbusConnectionState.Faulted, ModbusConnectionState.Stopped, true, true, false, false, false, true },
            { ModbusConnectionState.Starting, ModbusConnectionState.Stopped, false, true, true, false, true, false }
        };

    [Theory]
    [MemberData(nameof(ButtonStates))]
    public void ButtonStatesReflectRuntimeStatus(
        ModbusConnectionState clientState,
        ModbusConnectionState serverState,
        bool canStartClient,
        bool canStartServer,
        bool canStopClient,
        bool canStopServer,
        bool canStopBoth,
        bool canOpenSettings)
    {
        using var viewModel = CreateViewModel(clientState, serverState, out _);

        Assert.Equal(canStartClient, viewModel.CanStartClient);
        Assert.Equal(canStartServer, viewModel.CanStartServer);
        Assert.Equal(canStopClient, viewModel.CanStopClient);
        Assert.Equal(canStopServer, viewModel.CanStopServer);
        Assert.Equal(canStopBoth, viewModel.CanStopBoth);
        Assert.Equal(canOpenSettings, viewModel.CanOpenSettings);
    }

    [Fact]
    public void ConstructorDoesNotStartRuntime()
    {
        using var viewModel = CreateViewModel(
            ModbusConnectionState.Stopped,
            ModbusConnectionState.Stopped,
            out var runtime);

        Assert.Empty(runtime.Calls);
        Assert.True(viewModel.CanStartClient);
        Assert.True(viewModel.CanStartServer);
    }

    [Fact]
    public async Task SettingsCommandUsesMainSectionAndAppliesSavedOptions()
    {
        using var viewModel = CreateViewModel(
            ModbusConnectionState.Stopped,
            ModbusConnectionState.Stopped,
            out _,
            out var dialog);
        var savedOptions = CreateOptions();
        savedOptions.Client.Port = 2502;
        dialog.NextModbusResult = savedOptions;

        await viewModel.OpenSettingsCommand.Execute().FirstAsync().ToTask();

        Assert.Equal(ModbusOptions.SectionName, dialog.LastModbusSectionName);
        Assert.Equal(2502, viewModel.ClientPort);
    }

    private static ModbusViewModel CreateViewModel(
        ModbusConnectionState clientState,
        ModbusConnectionState serverState,
        out FakeRuntimeService runtime)
        => CreateViewModel(clientState, serverState, out runtime, out _);

    private static ModbusViewModel CreateViewModel(
        ModbusConnectionState clientState,
        ModbusConnectionState serverState,
        out FakeRuntimeService runtime,
        out FakeDialogService dialog)
    {
        runtime = new FakeRuntimeService(new ModbusStatus
        {
            ClientState = clientState,
            ServerState = serverState,
            ClientMessage = clientState.ToString(),
            ServerMessage = serverState.ToString()
        });
        dialog = new FakeDialogService();
        var options = CreateOptions();

        return new ModbusViewModel(
            runtime,
            new FakeClientService(),
            new FakeServerService(),
            new TestOptionsMonitor(options),
            new FakeAppConfigService(options),
            dialog);
    }

    private static ModbusOptions CreateOptions()
        => new()
        {
            AutostartOnWorkspaceOpen = false,
            StartupMode = ModbusRunMode.None,
            Client = new ModbusEndpointOptions
            {
                Host = "127.0.0.1",
                Port = 502,
                UnitId = 1,
                CoilCount = 4,
                RegisterCount = 4
            },
            Server = new ModbusEndpointOptions
            {
                Enabled = true,
                BindAddress = "127.0.0.1",
                Port = 502,
                UnitId = 1,
                CoilCount = 4,
                RegisterCount = 4
            }
        };

    private sealed class TestOptionsMonitor(ModbusOptions currentValue) : IOptionsMonitor<ModbusOptions>
    {
        public ModbusOptions CurrentValue { get; } = currentValue;

        public ModbusOptions Get(string? name)
            => CurrentValue;

        public IDisposable? OnChange(Action<ModbusOptions, string?> listener)
            => null;
    }

    private sealed class FakeAppConfigService(ModbusOptions options) : IAppConfigService
    {
        public T GetSection<T>(string sectionName) where T : class, new()
            => options.Clone() as T ?? new T();

        public string GetValue(string key)
            => string.Empty;

        public Task SaveSectionAsync<T>(string sectionName, T value, CancellationToken ct = default)
            => Task.CompletedTask;

        public void SaveUserSettings(UserSettings settings)
        {
        }

        public UserSettings LoadUserSettings()
            => new();
    }

    private sealed class FakeDialogService : IDialogService
    {
        public string? LastModbusSectionName { get; private set; }

        public ModbusOptions? NextModbusResult { get; set; }

        public Task<bool> ConfirmAsync(string message, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<string?> RequestSecretAsync(string message, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task ShowErrorAsync(string title, string message, string? details = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<ModbusOptions?> EditModbusSettingsAsync(
            string title,
            string sectionName,
            ModbusOptions options,
            CancellationToken ct = default)
        {
            LastModbusSectionName = sectionName;
            return Task.FromResult(NextModbusResult?.Clone());
        }

        public Task<OpcUaConfiguredTag?> EditOpcUaTagAsync(
            string title,
            OpcUaConfiguredTag? tag,
            OpcUaImportTarget target,
            CancellationToken ct = default)
            => Task.FromResult<OpcUaConfiguredTag?>(null);

        public Task<OpcUaTagImportResult?> ImportOpcUaTagsAsync(
            OpcUaBrowseRequest request,
            CancellationToken ct = default)
            => Task.FromResult<OpcUaTagImportResult?>(null);
    }

    private sealed class FakeRuntimeService(ModbusStatus status) : IModbusRuntimeService
    {
        public List<string> Calls { get; } = [];
        public ModbusStatus Status { get; private set; } = status;
        public ModbusSnapshot ClientSnapshot { get; } = ModbusSnapshot.Empty;
        public ModbusSnapshot ServerSnapshot { get; } = ModbusSnapshot.Empty;
        public ModbusOptions CurrentOptions { get; private set; } = new();
        public event EventHandler<ModbusStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<ModbusSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task StartAsync(ModbusRunMode mode = ModbusRunMode.Both, ModbusOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add($"Start:{mode}");
            CurrentOptions = options?.Clone() ?? CurrentOptions;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("Stop");
            return Task.CompletedTask;
        }

        public Task RestartAsync(ModbusRunMode mode = ModbusRunMode.Both, ModbusOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add($"Restart:{mode}");
            CurrentOptions = options?.Clone() ?? CurrentOptions;
            return Task.CompletedTask;
        }

        public Task StartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add("StartClient");
            CurrentOptions = options?.Clone() ?? CurrentOptions;
            return Task.CompletedTask;
        }

        public Task StopClientAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("StopClient");
            return Task.CompletedTask;
        }

        public Task RestartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add("RestartClient");
            CurrentOptions = options?.Clone() ?? CurrentOptions;
            return Task.CompletedTask;
        }

        public Task StartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add("StartServer");
            CurrentOptions = options?.Clone() ?? CurrentOptions;
            return Task.CompletedTask;
        }

        public Task StopServerAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("StopServer");
            return Task.CompletedTask;
        }

        public Task RestartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add("RestartServer");
            CurrentOptions = options?.Clone() ?? CurrentOptions;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

    private sealed class FakeClientService : IModbusClientService
    {
        public ModbusConnectionState State { get; } = ModbusConnectionState.Stopped;
        public ModbusSnapshot Snapshot { get; } = ModbusSnapshot.Empty;
        public event EventHandler<ModbusStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<ModbusSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task StartAsync(ModbusEndpointOptions options, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteCoilAsync(int address, bool value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteRegisterAsync(int address, ushort value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteRegistersAsync(int startAddress, ushort[] values, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeServerService : IModbusServerService
    {
        public ModbusConnectionState State { get; } = ModbusConnectionState.Stopped;
        public ModbusSnapshot Snapshot { get; } = ModbusSnapshot.Empty;
        public event EventHandler<ModbusStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<ModbusSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task StartAsync(ModbusEndpointOptions options, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetCoilAsync(int address, bool value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetRegisterAsync(int address, ushort value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetRegistersAsync(int startAddress, ushort[] values, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
