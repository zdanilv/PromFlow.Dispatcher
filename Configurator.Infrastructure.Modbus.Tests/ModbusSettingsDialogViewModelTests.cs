using Configurator.Application.Services;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Desktop.Dialogs.ModbusSettingsDialog;
using ReactiveUI.Builder;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed class ModbusSettingsDialogViewModelTests
{
    static ModbusSettingsDialogViewModelTests()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();
    }

    [Fact]
    public async Task SaveCommandPersistsValidOptionsAndReturnsResult()
    {
        var config = new FakeAppConfigService();
        var viewModel = new ModbusSettingsDialogViewModel(
            "Настройки",
            ModbusOptions.DemoSectionName,
            CreateOptions(),
            config,
            new ModbusDataMapValidator());
        viewModel.ClientPort = 1503;
        var resultTask = viewModel.Result.Take(1).ToTask();

        await viewModel.SaveCommand.Execute().FirstAsync().ToTask();
        var result = await resultTask;

        Assert.Equal(ModbusOptions.DemoSectionName, config.LastSectionName);
        Assert.Equal(1503, config.LastSavedOptions?.Client.Port);
        Assert.Equal(1503, result?.Client.Port);
        Assert.Empty(viewModel.ErrorText);
    }

    [Fact]
    public async Task SaveCommandShowsValidationErrorAndDoesNotPersistInvalidOptions()
    {
        var config = new FakeAppConfigService();
        var viewModel = new ModbusSettingsDialogViewModel(
            "Настройки",
            ModbusOptions.SectionName,
            CreateOptions(),
            config,
            new ModbusDataMapValidator())
        {
            ClientPort = 0
        };

        await viewModel.SaveCommand.Execute().FirstAsync().ToTask();

        Assert.Null(config.LastSavedOptions);
        Assert.Contains("порт", viewModel.ErrorText, StringComparison.OrdinalIgnoreCase);
    }

    private static ModbusOptions CreateOptions()
        => new()
        {
            WriteConfirmationTimeoutMs = 2000,
            Client = new ModbusEndpointOptions
            {
                Enabled = true,
                Host = "127.0.0.1",
                Port = 1502,
                UnitId = 1,
                PollIntervalMs = 500,
                CoilsEnabled = true,
                HoldingRegistersEnabled = true,
                CoilCount = 4,
                RegisterCount = 4
            },
            Server = new ModbusEndpointOptions
            {
                Enabled = true,
                BindAddress = "127.0.0.1",
                Port = 1502,
                UnitId = 1,
                PollIntervalMs = 500,
                CoilsEnabled = true,
                HoldingRegistersEnabled = true,
                CoilCount = 4,
                RegisterCount = 4
            },
            DataMap =
            [
                new()
                {
                    Name = "DemoButton",
                    Area = ModbusDataArea.Coil,
                    Address = 0,
                    Length = 1,
                    Access = ModbusDataAccess.ReadWrite,
                    Type = ModbusValueType.Bool
                }
            ]
        };

    private sealed class FakeAppConfigService : IAppConfigService
    {
        public string? LastSectionName { get; private set; }

        public ModbusOptions? LastSavedOptions { get; private set; }

        public T GetSection<T>(string sectionName) where T : class, new()
            => new();

        public string GetValue(string key)
            => string.Empty;

        public Task SaveSectionAsync<T>(string sectionName, T value, CancellationToken ct = default)
        {
            LastSectionName = sectionName;
            LastSavedOptions = (value as ModbusOptions)?.Clone();
            return Task.CompletedTask;
        }

        public void SaveUserSettings(UserSettings settings)
        {
        }

        public UserSettings LoadUserSettings()
            => new();
    }
}
