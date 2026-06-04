using Configurator.Application.Services;
using Configurator.Application.Services.Dialogs;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Desktop.Workspace.ModbusDemo;

namespace Configurator.Infrastructure.Modbus.Tests;

internal static class ModbusDemoTestContext
{
    public static ModbusCommandBitRow FindCommand(
        ModbusDemoViewModel viewModel,
        string pointName,
        int bitIndex)
        => viewModel.CommandGroups
            .SelectMany(group => group.Rows)
            .Single(row => row.PointName == pointName && row.BitIndex == bitIndex);

    public static ModbusOptions CreateOptions()
        => new()
        {
            Client = CreateEndpoint(),
            Server = CreateEndpoint(),
            DataMap =
            [
                CreatePoint("Telemetry_1", 0, ModbusDataAccess.Read),
                CreatePoint("Telemetry_2", 1, ModbusDataAccess.Read),
                CreatePoint("Telemetry_3", 2, ModbusDataAccess.Read),
                CreatePoint("Telemetry_4", 3, ModbusDataAccess.Read),
                CreatePoint("Commands_1", 4, ModbusDataAccess.Write),
                CreatePoint("Commands_2", 5, ModbusDataAccess.Write),
                CreatePoint("Commands_3", 6, ModbusDataAccess.Write),
                CreatePoint("Commands_4", 7, ModbusDataAccess.Write),
                CreatePoint("MB_ТЕКУЩАЯ_ПОЗИЦИЯ", 16, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_N_АВАРИЯ-ПОЗ_УПРАВ", 17, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_N_АВАРИЯ-ВРАЩЕНИЕ", 18, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_СТАТУС-ВРАЩЕНИЕ", 19, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_Hz", 20, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_ЦЕЛЬ_ПОЗИЦИЯ", 27, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_ВОЗВРАТ_ПОЗИЦИЯ", 28, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_ТОП_СБРОС-ПОЗ_УПРАВ", 29, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_ТОП_ФИЛЬТР-ПОЗ_УПРАВ", 30, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_ТОП_АВАРИЯ-ПОЗ_УПРАВ", 31, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_ТОП_ПАУЗА-ВРАЩЕНИЕ", 32, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_ТОП_АВАРИЯ-ВРАЩЕНИЕ", 33, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_ТОП_СБРОС-З_ВЫГРУЗКА", 34, ModbusDataAccess.ReadWrite),
                CreatePoint("MB_ТОП_СБРОС-З_ЗАГРУЗКА", 35, ModbusDataAccess.ReadWrite)
            ]
        };

    public static ModbusDemoViewModel CreateViewModel(
        FakeModbusTcpService service,
        FakeDialogService? dialog = null,
        Action<Action>? uiDispatcher = null)
    {
        var options = CreateOptions();
        return new ModbusDemoViewModel(
            service,
            new TestOptionsProvider(options),
            dialog ?? new FakeDialogService(),
            new FakeAppConfigService(options),
            uiDispatcher ?? (action => action()));
    }

    public static async Task<bool> WaitForAsync(Func<bool> condition)
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

    private static ModbusEndpointOptions CreateEndpoint()
        => new()
        {
            CoilsEnabled = false,
            HoldingRegistersEnabled = true,
            CoilStartAddress = 0,
            HoldingRegisterStartAddress = 16384,
            CoilCount = 0,
            RegisterCount = 36
        };

    private static ModbusDataPointOptions CreatePoint(
        string name,
        int address,
        ModbusDataAccess access)
        => new()
        {
            Name = name,
            Area = ModbusDataArea.HoldingRegister,
            Address = address,
            Length = 1,
            Access = access,
            Type = ModbusValueType.UInt16
        };

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

    public sealed class FakeModbusTcpService : IModbusDemoTcpService
    {
        private readonly Dictionary<string, List<Action<ModbusDataValue>>> _subscriptions = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Name, ushort Value)> SetCalls { get; } = [];

        public int StartClientCount { get; private set; }

        public int StartServerCount { get; private set; }

        public int StopCount { get; private set; }

        public ModbusOptions? LastStartClientOptions { get; private set; }

        public ModbusOptions? LastStartServerOptions { get; private set; }

        public TimeSpan StartClientSynchronousDelay { get; init; }

        public bool PublishStartingStateOnStartClient { get; init; }

        public bool BlockStartClientUntilCanceled { get; init; }

        public bool StartClientObservedCancellation { get; private set; }

        public ModbusOperationResult NextSetResult { get; set; } = ModbusOperationResult.Success();

        public ModbusServiceState State { get; private set; } = ModbusServiceState.Stopped;

        public event EventHandler<ModbusServiceState>? StateChanged;

        public async Task<ModbusOperationResult> StartClientAsync(ModbusOptions? options = null, CancellationToken ct = default)
        {
            StartClientCount++;
            LastStartClientOptions = options?.Clone();

            if (PublishStartingStateOnStartClient)
            {
                PublishState(new ModbusServiceState(
                    ModbusRunMode.Client,
                    ModbusConnectionState.Starting,
                    ModbusConnectionState.Stopped,
                    true,
                    "Клиент подключается",
                    null,
                    DateTimeOffset.Now));
            }

            if (BlockStartClientUntilCanceled)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    StartClientObservedCancellation = true;
                    throw;
                }
            }

            if (StartClientSynchronousDelay > TimeSpan.Zero)
            {
                Thread.Sleep(StartClientSynchronousDelay);
            }

            return ModbusOperationResult.Success();
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
            SetCalls.Add((name, Convert.ToUInt16(value)));
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

    public sealed class FakeDialogService : IDialogService
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
