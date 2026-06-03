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
using Configurator.Desktop.Workspace.ModbusDemo;
using ReactiveUI.Builder;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed class ModbusDemoViewModelTests
{
    static ModbusDemoViewModelTests()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();
    }

    [Fact]
    public async Task ToggleWritesDemoButton()
    {
        var service = new FakeModbusTcpService();
        var dialog = new FakeDialogService();
        using var viewModel = CreateViewModel(service, dialog);

        viewModel.DemoButton = true;

        Assert.True(await WaitForAsync(() => service.SetCalls.Count == 1));
        Assert.Equal("DemoButton", service.SetCalls[0].Name);
        Assert.Equal(true, service.SetCalls[0].Value);
        Assert.Empty(dialog.Errors);
    }

    [Fact]
    public void ButtonStatesReflectDemoFacadeState()
    {
        var service = new FakeModbusTcpService();
        using var viewModel = CreateViewModel(service);

        Assert.True(viewModel.CanStartClient);
        Assert.True(viewModel.CanStartServer);
        Assert.False(viewModel.CanStop);
        Assert.True(viewModel.CanOpenSettings);

        service.PublishState(new ModbusServiceState(
            ModbusRunMode.Both,
            ModbusConnectionState.Running,
            ModbusConnectionState.Running,
            false,
            "Client and server running",
            null,
            DateTimeOffset.Now));

        Assert.False(viewModel.CanStartClient);
        Assert.False(viewModel.CanStartServer);
        Assert.True(viewModel.CanStop);
        Assert.False(viewModel.CanOpenSettings);
    }

    [Fact]
    public async Task StartCommandsCallDemoFacade()
    {
        var service = new FakeModbusTcpService();
        using var viewModel = CreateViewModel(service);

        await viewModel.StartClientCommand.Execute().FirstAsync().ToTask();
        await viewModel.StartServerCommand.Execute().FirstAsync().ToTask();

        Assert.Equal(1, service.StartClientCount);
        Assert.Equal(1, service.StartServerCount);
    }

    [Fact]
    public async Task ToggleInBothModeDoesNotShowModalErrorWhenWriteSucceeds()
    {
        var service = new FakeModbusTcpService();
        var dialog = new FakeDialogService();
        using var viewModel = CreateViewModel(service, dialog);
        service.PublishState(new ModbusServiceState(
            ModbusRunMode.Both,
            ModbusConnectionState.Running,
            ModbusConnectionState.Running,
            false,
            "Client and server running",
            null,
            DateTimeOffset.Now));

        viewModel.DemoButton = true;

        Assert.True(await WaitForAsync(() => service.SetCalls.Count == 1));
        Assert.Equal("DemoButton", service.SetCalls[0].Name);
        Assert.Empty(dialog.Errors);
        Assert.Contains("Both", viewModel.StatusText);
    }

    [Fact]
    public async Task SaveInputShowsModalErrorWhenWriteFails()
    {
        var service = new FakeModbusTcpService
        {
            NextSetResult = ModbusOperationResult.Failure("WriteFailed", "Write failed.", "No ACK")
        };
        var dialog = new FakeDialogService();
        using var viewModel = CreateViewModel(service, dialog);
        viewModel.DemoInputText = "5";

        await viewModel.SaveInputCommand.Execute().FirstAsync().ToTask();

        Assert.Single(service.SetCalls);
        Assert.Single(dialog.Errors);
        Assert.Contains("Write failed", dialog.Errors[0].Message);
    }

    [Fact]
    public void ImageVisibilityChangesFromSubscription()
    {
        var service = new FakeModbusTcpService();
        using var viewModel = CreateViewModel(service);

        service.Publish("DemoImageVisible", (ushort)1);
        Assert.True(viewModel.IsDemoImageVisible);

        service.Publish("DemoImageVisible", (ushort)0);
        Assert.False(viewModel.IsDemoImageVisible);
    }

    [Fact]
    public async Task SettingsCommandUsesDemoSectionAndNextStartUsesSavedOptions()
    {
        var service = new FakeModbusTcpService();
        var dialog = new FakeDialogService();
        var savedOptions = CreateOptions();
        savedOptions.Client.Port = 2503;
        dialog.NextModbusResult = savedOptions;
        using var viewModel = CreateViewModel(service, dialog);

        await viewModel.OpenSettingsCommand.Execute().FirstAsync().ToTask();
        await viewModel.StartClientCommand.Execute().FirstAsync().ToTask();

        Assert.Equal(ModbusOptions.DemoSectionName, dialog.LastModbusSectionName);
        Assert.Equal(2503, service.LastStartClientOptions?.Client.Port);
    }

    private static ModbusOptions CreateOptions()
        => new()
        {
            DataMap =
            [
                new() { Name = "DemoButton", Area = ModbusDataArea.Coil, Address = 0, Type = ModbusValueType.Bool, Access = ModbusDataAccess.ReadWrite },
                new() { Name = "DemoInput", Area = ModbusDataArea.HoldingRegister, Address = 0, Type = ModbusValueType.UInt16, Access = ModbusDataAccess.ReadWrite },
                new() { Name = "DemoImageVisible", Area = ModbusDataArea.HoldingRegister, Address = 0, Type = ModbusValueType.UInt16, Access = ModbusDataAccess.Read }
            ]
        };

    private static ModbusDemoViewModel CreateViewModel(
        FakeModbusTcpService service,
        FakeDialogService? dialog = null)
    {
        var options = CreateOptions();
        return new ModbusDemoViewModel(
            service,
            new TestOptionsProvider(options),
            dialog ?? new FakeDialogService(),
            new FakeAppConfigService(options));
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        var startedAt = DateTimeOffset.UtcNow;

        while (DateTimeOffset.UtcNow - startedAt < TimeSpan.FromSeconds(2))
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return false;
    }

    private sealed class TestOptionsProvider(ModbusOptions currentValue) : IModbusDemoOptionsProvider
    {
        public ModbusOptions CurrentValue { get; } = currentValue;
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

    private sealed class FakeModbusTcpService : IModbusDemoTcpService
    {
        private readonly Dictionary<string, List<Action<ModbusDataValue>>> _subscriptions = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Name, object? Value)> SetCalls { get; } = [];

        public int StartClientCount { get; private set; }

        public int StartServerCount { get; private set; }

        public int StopCount { get; private set; }

        public ModbusOptions? LastStartClientOptions { get; private set; }

        public ModbusOptions? LastStartServerOptions { get; private set; }

        public ModbusOperationResult NextSetResult { get; set; } = ModbusOperationResult.Success();

        public ModbusServiceState State { get; private set; } = ModbusServiceState.Stopped;

        public event EventHandler<ModbusServiceState>? StateChanged;

        public Task<ModbusOperationResult> StartClientAsync(ModbusOptions? options = null, CancellationToken ct = default)
        {
            StartClientCount++;
            LastStartClientOptions = options?.Clone();
            return Task.FromResult(ModbusOperationResult.Success());
        }

        public Task<ModbusOperationResult> StartServerAsync(ModbusOptions? options = null, CancellationToken ct = default)
        {
            StartServerCount++;
            LastStartServerOptions = options?.Clone();
            return Task.FromResult(ModbusOperationResult.Success());
        }

        public Task<ModbusOperationResult> StopAsync(CancellationToken ct = default)
        {
            StopCount++;
            return Task.FromResult(ModbusOperationResult.Success());
        }

        public Task<ModbusOperationResult<T>> GetAsync<T>(string name, CancellationToken ct = default)
            => Task.FromResult(ModbusOperationResult<T>.Failure("NotImplemented", "Not implemented."));

        public Task<ModbusOperationResult> SetAsync<T>(string name, T value, CancellationToken ct = default)
        {
            SetCalls.Add((name, value));
            return Task.FromResult(NextSetResult);
        }

        public IDisposable Subscribe(string name, Action<ModbusDataValue> onChanged)
        {
            if (!_subscriptions.TryGetValue(name, out var subscribers))
            {
                subscribers = [];
                _subscriptions[name] = subscribers;
            }

            subscribers.Add(onChanged);
            return new Subscription(() => subscribers.Remove(onChanged));
        }

        public void Publish(string name, object? value)
        {
            if (!_subscriptions.TryGetValue(name, out var subscribers))
            {
                return;
            }

            var dataValue = new ModbusDataValue(
                name,
                ModbusDataArea.HoldingRegister,
                0,
                1,
                ModbusValueType.UInt16,
                value,
                DateTimeOffset.Now);
            foreach (var subscriber in subscribers.ToArray())
            {
                subscriber(dataValue);
            }
        }

        public void PublishState(ModbusServiceState state)
        {
            State = state;
            StateChanged?.Invoke(this, state);
        }

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;

        private sealed class Subscription(Action dispose) : IDisposable
        {
            public void Dispose()
                => dispose();
        }
    }

    private sealed class FakeDialogService : IDialogService
    {
        public List<(string Title, string Message, string? Details)> Errors { get; } = [];

        public string? LastModbusSectionName { get; private set; }

        public ModbusOptions? NextModbusResult { get; set; }

        public Task<bool> ConfirmAsync(string message, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<string?> RequestSecretAsync(string message, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task ShowErrorAsync(string title, string message, string? details = null, CancellationToken ct = default)
        {
            Errors.Add((title, message, details));
            return Task.CompletedTask;
        }

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
}
