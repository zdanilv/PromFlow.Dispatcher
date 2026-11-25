using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Configurator.Application.Services;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Panels;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Configurator.Desktop.Workspace.RouteMap.SignalMapping;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;
using Configurator.Infrastructure.Modbus.RouteMap;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Tests.RouteMap.Ui;

public sealed partial class RouteMapSettingsDialogVisualTests
{
    [AvaloniaTheory]
    [InlineData(ApplicationOptions.AdminWorkMode, false)]
    [InlineData(ApplicationOptions.UserWorkMode, false)]
    [InlineData(ApplicationOptions.AdminWorkMode, true)]
    public async Task Saved_TopBar_commands_route_UI_clicks_to_Modbus_pulses(string workMode, bool failReset)
    {
        var directory = Directory.CreateTempSubdirectory("route-map-topbar-");
        var storage = new RouteMapConfigurationStorage(Path.Combine(directory.FullName, "route-map.json"));
        var seed = RouteMapSeed.Create();
        var mapper = new RouteMapConfigurationMapper(seed);
        var document = mapper.CreateSeedDocument();
        var configured = new[] { document.TopBar.Unload, document.TopBar.Rotation, document.TopBar.Load };
        string[] names = ["UnloadButton", "RotationButton", "LoadButton"];
        string[] signalIds = ["test.topbar.unload", "test.topbar.rotate", "test.topbar.load"];
        string[] captions = ["Тест выгрузки", "Тест вращения", "Тест загрузки"];
        var modbus = new ModbusOptions();
        modbus.DataMap.Clear();
        for (var i = 0; i < configured.Length; i++)
        {
            configured[i].Text = captions[i];
            configured[i].Bindings[0].SignalId = signalIds[i];
            modbus.DataMap.Add(new ModbusDataPointOptions
            {
                Name = signalIds[i], Area = ModbusDataArea.Coil, Address = i,
                Access = ModbusDataAccess.ReadWrite, Type = ModbusValueType.Bool,
                WriteMode = ModbusWriteMode.Pulse, PulseDurationMs = 25,
            });
        }

        try
        {
            await storage.SaveActiveAsync(document);
            using var manager = new RouteMapConfigurationManager(storage, mapper,
                new RouteMapConfigurationValidator(), new RouteMapConfigurationMigrator());
            Assert.Null(manager.LastLoadError);
            Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, manager.CurrentDocument.SchemaVersion);
            var inventory = RouteMapSignalInventory.Build(manager.CurrentDefinition);
            foreach (var signalId in signalIds)
            {
                var row = Assert.Single(inventory, item => item.SignalId == signalId);
                Assert.Equal(RouteMapSignalElementCategory.TopBar, row.Category);
                Assert.Equal(SignalValueType.Bool, row.ExpectedType);
                Assert.Equal(ModbusDataAccess.ReadWrite, row.RequiredAccess);
                Assert.True(row.RequiresPulseWriteMode);
            }

            using var signals = new Subject<IReadOnlyDictionary<string, SignalValue>>();
            await using var transport = new RecordingPulseService(failReset);
            var options = new StaticOptionsMonitor(modbus);
            var dispatcher = new ModbusTcpCommandDispatcher(transport, options);
            var mockDispatcher = new NoOpCommandDispatcher();
            using var runtime = new RouteMapSignalRuntime(new EmptySignalProvider(), mockDispatcher,
                new TestSignalProvider(signals), dispatcher, RouteMapSignalSource.Modbus);
            var journal = new RouteMapSessionJournal();
            var notifications = new NotificationsPanelViewModel(journal, new NoOpDialogService(), new NoOpBitWriter());
            using var dashboard = new RouteMapDashboardViewModel(manager, runtime, new RouteMapRuntimeMapper(manager),
                runtime, new NoOpSettingsDialogService(), notifications, journal, options,
                Options.Create(new ApplicationOptions { WorkMode = workMode }));

            var topBar = dashboard.TopBar;
            var commands = new[] { topBar.UnloadCommand, topBar.RotationCommand, topBar.LoadCommand };
            var view = new TopBarView { Height = 64, DataContext = topBar };
            var window = new Window { Width = 1366, Height = 120, Content = view };
            window.Show();
            try
            {
                var admin = workMode == ApplicationOptions.AdminWorkMode;
                var connectionId = RouteMapSystemSignalIds.ConnectionConnected;
                signals.OnNext(new Dictionary<string, SignalValue>
                {
                    [connectionId] = new(connectionId, true, SignalValueType.Bool, DateTimeOffset.UtcNow, true, false),
                });
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Empty(transport.Attempts);

                var expected = new List<(string SignalId, bool Value)>();
                for (var i = 0; i < names.Length; i++)
                {
                    var button = Assert.IsType<Button>(view.FindControl<Button>(names[i]));
                    Assert.Equal(captions[i], button.Content);
                    Assert.Same(commands[i], button.Command);
                    Assert.Equal(admin, button.IsEffectivelyVisible);
                    if (!admin)
                    {
                        Assert.False(button.Focus(NavigationMethod.Tab));
                        await commands[i].Execute().ToTask().WaitAsync(TimeSpan.FromSeconds(5));
                        continue;
                    }

                    Assert.True(button.IsEffectivelyEnabled);
                    var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    button.PropertyChanged += (_, args) =>
                    {
                        if (args.Property == InputElement.IsEffectivelyEnabledProperty && button.IsEffectivelyEnabled)
                            finished.TrySetResult();
                    };
                    var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                    window.MouseMove(point);
                    window.MouseDown(point, MouseButton.Left);
                    window.MouseUp(point, MouseButton.Left);
                    await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

                    expected.Add((signalIds[i], true));
                    expected.Add((signalIds[i], false));
                    Assert.Equal(expected, transport.Attempts);
                    Assert.Equal(failReset, transport.Values[signalIds[i]]);
                    Assert.True(button.IsEffectivelyEnabled);
                    if (failReset)
                        Assert.Contains("PLC command state is unknown", dashboard.CommandErrorMessage);
                    else
                        Assert.Null(dashboard.CommandErrorMessage);
                }

                Assert.Equal(expected, transport.Attempts);
                Assert.Equal(admin ? signalIds : Array.Empty<string>(), transport.Values.Keys);
                Assert.Empty(mockDispatcher.Requests);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class RecordingPulseService(bool failReset) : IModbusTcpService
    {
        public List<(string SignalId, bool Value)> Attempts { get; } = [];
        public Dictionary<string, bool> Values { get; } = [];

        public Task<ModbusOperationResult> SetAsync<T>(string name, T value, CancellationToken ct = default)
        {
            var edge = Assert.IsType<bool>(value);
            Attempts.Add((name, edge));
            if (!edge && failReset)
                return Task.FromResult(ModbusOperationResult.Failure("Transport", "Test falling edge was rejected."));

            Values[name] = edge;
            return Task.FromResult(ModbusOperationResult.Success());
        }

        public ModbusServiceState State => ModbusServiceState.Stopped;
        public event EventHandler<ModbusServiceState>? StateChanged { add { } remove { } }
        public Task<ModbusOperationResult> StartClientAsync(ModbusOptions? options = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ModbusOperationResult> StartServerAsync(ModbusOptions? options = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ModbusOperationResult> StopAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ModbusOperationResult<T>> GetAsync<T>(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public IDisposable Subscribe(string name, Action<ModbusDataValue> onChanged) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
