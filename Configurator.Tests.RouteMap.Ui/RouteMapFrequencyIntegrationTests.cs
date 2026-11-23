using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Configurator.Application.Services;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Configurator.Desktop.Workspace.RouteMap.SignalMapping;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;
using Configurator.Infrastructure.Modbus;
using Configurator.Infrastructure.Modbus.RouteMap;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Tests.RouteMap.Ui;

public sealed partial class RouteMapSettingsDialogVisualTests
{
    [AvaloniaFact]
    public void Frequency_settings_edit_a_read_binding_and_refresh_the_mapping_inventory()
    {
        const string id = "site.frequency";
        using var fixture = new DialogFixture(1320, 780);
        var options = new ModbusOptions
        {
            DataMap = [new() { Name = id, Area = ModbusDataArea.HoldingRegister, Type = ModbusValueType.UInt16, Length = 1, Access = ModbusDataAccess.Read }],
        };
        using var mapping = new RouteMapSignalMappingViewModel(fixture.Manager, new StaticOptionsMonitor(options),
            new NullAppConfigService(), new ModbusDataMapValidator(), new NoOpDataMapRuntime());
        fixture.SelectTab(1);

        var add = fixture.Dialog.FindControl<Button>("AddFrequencyBindingButton")!;
        var remove = fixture.Dialog.FindControl<Button>("RemoveFrequencyBindingButton")!;
        var signalId = fixture.Dialog.FindControl<TextBox>("FrequencySignalId")!;
        var type = fixture.Dialog.FindControl<ComboBox>("FrequencyValueType")!;
        Assert.True(add.IsEffectivelyVisible);
        Assert.False(remove.IsEffectivelyVisible);
        Assert.DoesNotContain(mapping.Rows, row => row.SignalId == id);

        add.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(add.IsEffectivelyVisible);
        Assert.True(remove.IsEffectivelyVisible);
        Assert.Equal(SignalValueType.Float32, type.SelectedItem);
        Assert.DoesNotContain(SignalValueType.Bool, type.Items.Cast<SignalValueType>());
        Assert.Equal(string.Empty, signalId.Text);
        fixture.ViewModel.Apply();
        Assert.Contains("SignalId", fixture.ViewModel.ErrorMessage);

        signalId.Text = id;
        type.SelectedItem = SignalValueType.UInt16;
        fixture.ViewModel.Apply();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(fixture.ViewModel.ErrorMessage);
        var row = Assert.Single(mapping.Rows, row => row.SignalId == id);
        Assert.Equal(nameof(SignalBindingRole.FrequencyFeedback), row.Roles);
        Assert.Equal(ModbusDataAccess.Read, row.RequiredAccess);
        Assert.Equal(ModbusValueType.UInt16, row.Type);
        Assert.True(row.IsMapped);

        remove.Command!.Execute(null);
        fixture.ViewModel.Apply();
        Dispatcher.UIThread.RunJobs();
        Assert.True(add.IsEffectivelyVisible);
        Assert.False(remove.IsEffectivelyVisible);
        Assert.DoesNotContain(mapping.Rows, row => row.SignalId == id);
        Assert.Equal(id, Assert.Single(mapping.UnusedPoints).Point.Name);
    }

    [AvaloniaTheory]
    [InlineData(ApplicationOptions.AdminWorkMode)]
    [InlineData(ApplicationOptions.UserWorkMode)]
    public async Task Frequency_overlay_reads_Modbus_loopback_and_clears_unavailable_values(string workMode)
    {
        const string signalId = "site.frequency";
        var cancellationToken = TestContext.Current.CancellationToken;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var endpoint = new ModbusEndpointOptions { Port = port, RegisterCount = 2, PollIntervalMs = 100 };
        var modbus = new ModbusOptions
        {
            Client = endpoint,
            DataMap = [new() { Name = signalId, Area = ModbusDataArea.HoldingRegister, Type = ModbusValueType.Real, Length = 2, Access = ModbusDataAccess.Read }],
        };
        using var config = new DialogFixture(1320, 780);
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IModbusDataMapValidator, ModbusDataMapValidator>();
        services.AddModbusInfrastructure(new ConfigurationBuilder().Build());
        await using var serverServices = services.BuildServiceProvider();
        await using var clientServices = services.BuildServiceProvider();
        var server = serverServices.GetRequiredService<IModbusServerService>();
        await server.StartAsync(endpoint, cancellationToken);
        await server.SetRegistersAsync(0, ModbusRegistersCodec.EncodeReal(47.5f), cancellationToken);
        var connection = clientServices.GetRequiredService<IModbusTcpService>();
        var options = new StaticOptionsMonitor(modbus);
        using var modbusSignals = new ModbusTcpSignalValueProvider(clientServices.GetRequiredService<IModbusDataSnapshotSource>(),
            connection, options, Options.Create(new RouteMapRuntimeOptions()), NullLogger<ModbusTcpSignalValueProvider>.Instance);
        using var mockSignals = new Subject<IReadOnlyDictionary<string, SignalValue>>();
        var mockDispatcher = new NoOpCommandDispatcher();
        var modbusDispatcher = new NoOpCommandDispatcher();
        using var runtime = new RouteMapSignalRuntime(new TestSignalProvider(mockSignals), mockDispatcher,
            modbusSignals, modbusDispatcher, RouteMapSignalSource.Modbus);
        var journal = new RouteMapSessionJournal();
        using var dashboard = new RouteMapDashboardViewModel(config.Manager, runtime, new RouteMapRuntimeMapper(config.Manager),
            runtime, new NoOpSettingsDialogService(), new NotificationsPanelViewModel(journal, new NoOpDialogService(), new NoOpBitWriter()),
            journal, options, Options.Create(new ApplicationOptions { WorkMode = workMode }));
        var view = new RouteMapDashboardView { DataContext = dashboard };
        var window = new Window { Width = 1366, Height = 768, Content = view };
        window.Show();

        try
        {
            var frequency = view.FindControl<Border>("FrequencyOverlay")!;
            var selected = view.FindControl<Border>("SelectedObjectOverlay")!;
            var text = view.FindControl<TextBlock>("FrequencyText")!;
            Assert.False(frequency.IsEffectivelyVisible);
            var initialSelectedPosition = selected.TranslatePoint(new Point(), view);
            config.ViewModel.AddFrequencyBindingCommand.Execute().Subscribe();
            config.ViewModel.Draft.Map.FrequencyBinding!.SignalId = signalId;
            config.ViewModel.Apply();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.True(frequency.IsEffectivelyVisible);
            Assert.False(frequency.IsHitTestVisible);
            Assert.True(frequency.Bounds.Right < selected.Bounds.Left);
            Assert.Equal(frequency.Bounds.Bottom, selected.Bounds.Bottom);
            Assert.Equal(initialSelectedPosition, selected.TranslatePoint(new Point(), view));
            Assert.Equal(selected.Padding, frequency.Padding);
            Assert.Equal(selected.Background, frequency.Background);
            Assert.Equal("— Hz", text.Text);

            var result = await connection.StartClientAsync(modbus, cancellationToken);
            Assert.True(result.Succeeded, result.ErrorMessage);
            var expected = $"{47.5f} Hz";
            await WaitForText(expected);
            dashboard.SelectedObjectId = "bsu_1";
            Assert.Equal(expected, text.Text);

            config.ViewModel.Draft.Map.FrequencyBinding.SignalId = "site.unmapped.frequency";
            config.ViewModel.Apply();
            await WaitForText("— Hz");
            config.ViewModel.Draft.Map.FrequencyBinding.SignalId = signalId;
            config.ViewModel.Apply();
            await WaitForText(expected);

            runtime.SwitchSource(RouteMapSignalSource.Mock);
            mockSignals.OnNext(new Dictionary<string, SignalValue>
            {
                [signalId] = new(signalId, 25f, SignalValueType.Float32, DateTimeOffset.UtcNow, true, false),
            });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("— Hz", text.Text);
            runtime.SwitchSource(RouteMapSignalSource.Modbus);
            await WaitForText(expected);

            await server.SetRegistersAsync(0, ModbusRegistersCodec.EncodeReal(0f), cancellationToken);
            await WaitForText("0 Hz");
            Assert.True((await connection.StopAsync(cancellationToken)).Succeeded);
            await WaitForText("— Hz");
            Assert.Empty(modbusDispatcher.Requests);
            Assert.Empty(mockDispatcher.Requests);

            Task<string?> WaitForText(string value) => text.GetObservable(TextBlock.TextProperty)
                .FirstAsync(current => current == value).ToTask(cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        finally
        {
            window.Close();
        }
    }
}
