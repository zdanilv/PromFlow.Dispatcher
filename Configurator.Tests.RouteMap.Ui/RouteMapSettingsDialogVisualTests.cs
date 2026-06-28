using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Configurator.Application.Services;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Application.Services.Authorization;
using Configurator.Desktop.Dialogs.ModbusSettingsDialog;
using Configurator.Desktop.Workspace.RouteMap;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Panels;
using Configurator.Desktop.Workspace.RouteMap.Settings;
using Configurator.Desktop.Workspace.RouteMap.SignalMapping;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;
using Configurator.Desktop.Workspace;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Tests.RouteMap.Ui;

public sealed class RouteMapSettingsDialogVisualTests
{
    [AvaloniaTheory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Binding_rows_have_five_non_overlapping_columns(int tabIndex)
    {
        using var fixture = new DialogFixture(1320, 780);
        fixture.SelectTab(tabIndex);

        var rows = fixture.Dialog.GetVisualDescendants()
            .OfType<Grid>()
            .Where(x => x.Classes.Contains("signal-binding-row"))
            .ToArray();

        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            var controls = row.Children.OfType<Control>().ToArray();
            Assert.Equal(5, controls.Length);
            Assert.Equal([0, 1, 2, 3, 4], controls.Select(Grid.GetColumn).ToArray());
            Assert.All(controls, control =>
            {
                Assert.True(control.IsVisible);
                Assert.True(control.Bounds.Width > 0);
                Assert.True(control.Bounds.Height > 0);
            });

            for (var index = 1; index < controls.Length; index++)
                Assert.True(controls[index - 1].Bounds.Right <= controls[index].Bounds.Left);
        }
    }

    [AvaloniaTheory]
    [InlineData(2, "Обычный фон")]
    [InlineData(2, "Pressed фон")]
    [InlineData(2, "Pressed текст")]
    [InlineData(3, "Положение подписи")]
    [InlineData(3, "Цвет сигнального контура")]
    [InlineData(3, "Толщина сигнального контура")]
    [InlineData(4, "Зазор от узлов")]
    [InlineData(4, "Края линии")]
    [InlineData(4, "Отрезки")]
    [InlineData(5, "ПУСК pressed текст")]
    [InlineData(5, "СТОП checked текст")]
    public void New_visual_properties_are_present_in_settings_tabs(int tabIndex, string label)
    {
        using var fixture = new DialogFixture(1320, 780);
        fixture.SelectTab(tabIndex);

        var labels = fixture.Dialog.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(x => x.Text)
            .ToArray();

        Assert.Contains(label, labels);
    }

    [AvaloniaFact]
    public void Narrow_binding_editor_uses_horizontal_scrolling_without_overlap()
    {
        var editor = new SignalBindingsEditor
        {
            Width = 620,
            Bindings = new[]
            {
                new SignalBindingConfiguration
                {
                    Role = SignalBindingRole.Visible,
                    SignalId = "node.visible",
                    Direction = SignalBindingDirection.Read,
                    ValueType = Configurator.Application.Services.Signals.SignalValueType.Bool,
                },
            },
        };
        var window = new Window { Width = 660, Height = 260, Content = editor };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var scroll = editor.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(x => x.Classes.Contains("signal-bindings-scroll"));
        var row = editor.GetVisualDescendants()
            .OfType<Grid>()
            .Single(x => x.Classes.Contains("signal-binding-row"));
        var controls = row.Children.OfType<Control>().ToArray();

        Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
        for (var index = 1; index < controls.Length; index++)
            Assert.True(controls[index - 1].Bounds.Right <= controls[index].Bounds.Left);

        window.Close();
    }

    [AvaloniaFact]
    public void All_settings_tabs_complete_layout_without_exceptions()
    {
        using var fixture = new DialogFixture(1320, 780);

        for (var tabIndex = 0; tabIndex < 7; tabIndex++)
        {
            fixture.SelectTab(tabIndex);
            fixture.Dialog.InvalidateMeasure();
            fixture.Dialog.InvalidateVisual();
            Dispatcher.UIThread.RunJobs();
            Assert.True(fixture.Dialog.Bounds.Width > 0);
            Assert.True(fixture.Dialog.Bounds.Height > 0);
        }
    }

    [AvaloniaFact]
    public void Workspace_places_signal_mapping_tab_immediately_after_route_map()
    {
        var view = new WorkspaceView { DataContext = CreateWorkspaceViewModel(isAdminMode: true) };
        var window = new Window { Width = 1200, Height = 760, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tabs = view.GetVisualDescendants().OfType<TabControl>().Single();
        var headers = tabs.Items.Cast<TabItem>().Select(item => item.Header?.ToString() ?? string.Empty).ToArray();

        Assert.True(tabs.IsVisible);
        Assert.Equal(["Route Map", "SignalId ↔ Modbus", "Modbus Demo"], headers);

        window.Close();
    }

    [AvaloniaFact]
    public void Workspace_user_mode_shows_route_map_without_tabs()
    {
        var view = new WorkspaceView { DataContext = CreateWorkspaceViewModel(isAdminMode: false) };
        var window = new Window { Width = 1200, Height = 760, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var root = Assert.IsType<Grid>(view.Content);
        var tabs = root.Children.OfType<TabControl>().Single();
        var routeMapHost = view.FindControl<Grid>("UserRouteMapHost")!;

        Assert.False(tabs.IsVisible);
        Assert.True(routeMapHost.IsVisible);
        Assert.Single(routeMapHost.Children.OfType<RouteMapDashboardView>());

        window.Close();
    }

    [AvaloniaFact]
    public void TopBar_does_not_show_admin_text_or_arrow()
    {
        var view = new TopBarView { DataContext = new TopBarViewModel() };
        var window = new Window { Width = 1200, Height = 96, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = view.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(textBlock => textBlock.Text)
            .ToArray();

        Assert.DoesNotContain("Admin", texts);
        Assert.DoesNotContain("⌄", texts);

        window.Close();
    }

    [AvaloniaFact]
    public void TopBar_disables_commands_but_keeps_settings_available_when_connection_is_offline()
    {
        var viewModel = new TopBarViewModel();
        viewModel.ApplyRuntime(
            isAutomaticMode: false,
            isManualMode: true,
            hasEmergency: false,
            connectionStatusText: "Offline",
            isConnectionAvailable: false);
        var view = new TopBarView { DataContext = viewModel };
        var window = new Window { Width = 1200, Height = 96, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.False(view.FindControl<ToggleButton>("AutomaticButton")!.IsEnabled);
        Assert.False(view.FindControl<ToggleButton>("ManualButton")!.IsEnabled);
        Assert.False(view.FindControl<ToggleButton>("EmergencyButton")!.IsEnabled);

        var settings = view.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.Classes.Contains("settings"));
        Assert.True(settings.IsVisible);
        Assert.True(settings.IsEnabled);

        window.Close();
    }

    [AvaloniaFact]
    public void TopBar_hides_settings_in_user_mode()
    {
        var view = new TopBarView { DataContext = new TopBarViewModel(isSettingsVisible: false) };
        var window = new Window { Width = 1200, Height = 96, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var settings = view.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.Classes.Contains("settings"));

        Assert.False(settings.IsVisible);

        window.Close();
    }

    [AvaloniaFact]
    public void RouteMap_node_menu_items_are_disabled_when_commands_are_disabled()
    {
        var control = new RouteMapControl { AreCommandsEnabled = false };

        var item = control.CreateNodeMenuItem("Отправить", "bsu_1", isChecked: false, command: null);

        Assert.False(item.IsEnabled);
    }

    [AvaloniaFact]
    public void Signal_mapping_groups_are_expanded_by_default_and_can_be_collapsed()
    {
        using var fixture = new SignalMappingFixture(1200, 760);
        var expanders = fixture.View.GetVisualDescendants()
            .OfType<Expander>()
            .Where(item => item.Classes.Contains("signal-group"))
            .ToArray();

        Assert.NotEmpty(expanders);
        Assert.All(expanders, item => Assert.True(item.IsExpanded));

        var first = expanders[0];
        var group = Assert.IsType<RouteMapSignalMappingGroup>(first.DataContext);
        first.IsExpanded = false;
        Dispatcher.UIThread.RunJobs();

        Assert.False(first.IsExpanded);
        Assert.False(group.IsExpanded);
    }

    [AvaloniaFact]
    public void Signal_mapping_uses_gray_background_only_for_unmapped_rows()
    {
        using var fixture = new SignalMappingFixture(1200, 760);
        var rowBorders = fixture.View.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("signal-mapping-row"))
            .ToArray();

        Assert.NotEmpty(rowBorders);
        var unmapped = rowBorders.First(border =>
            border.DataContext is RouteMapSignalMappingRow { IsSystem: false, IsMapped: false });
        var brush = Assert.IsType<SolidColorBrush>(unmapped.Background);
        Assert.Equal(Color.Parse("#F1F3F5"), brush.Color);

        Assert.All(
            rowBorders.Where(border => border.DataContext is RouteMapSignalMappingRow { IsSystem: true }),
            border => Assert.Same(Brushes.White, border.Background));
    }

    [AvaloniaFact]
    public void Signal_mapping_physical_addresses_are_editable_text_boxes()
    {
        using var fixture = new SignalMappingFixture(1200, 760);
        var rowGrid = FirstSignalMappingRowGrid(fixture.View);

        var addressInputs = rowGrid.Children
            .OfType<TextBox>()
            .Where(textBox => Grid.GetColumn(textBox) is 13 or 14)
            .ToArray();

        Assert.Equal(2, addressInputs.Length);
        Assert.All(addressInputs, input => Assert.NotNull(input.ContextMenu));
    }

    [AvaloniaFact]
    public void Signal_mapping_interactive_row_controls_have_context_menus()
    {
        using var fixture = new SignalMappingFixture(1200, 760);
        var rowGrid = FirstSignalMappingRowGrid(fixture.View);
        var actionGrid = rowGrid.Children
            .OfType<Grid>()
            .Single(grid => Grid.GetColumn(grid) == 15);
        var controls = rowGrid.Children
            .OfType<Control>()
            .Concat(actionGrid.Children.OfType<Control>())
            .Where(control => control.IsVisible)
            .Where(control => control is ComboBox or TextBox or Button)
            .ToArray();

        Assert.NotEmpty(controls);
        Assert.All(controls, control => Assert.NotNull(control.ContextMenu));
    }

    [AvaloniaFact]
    public void Signal_mapping_node_register_address_enables_bit_input()
    {
        using var fixture = new SignalMappingFixture(1200, 760);
        var rowGrid = SignalMappingRowGrid(fixture.View, "bsu_1.fault");
        var row = Assert.IsType<RouteMapSignalMappingRow>(rowGrid.DataContext);
        row.IsMapped = true;

        row.ClientPhysicalAddressText = "16420";
        Dispatcher.UIThread.RunJobs();

        var bitInput = rowGrid.Children
            .OfType<TextBox>()
            .Single(textBox => Grid.GetColumn(textBox) == 10);
        Assert.True(bitInput.IsEnabled);
    }

    [AvaloniaTheory]
    [InlineData("active_bsu1_bsu2.fault")]
    [InlineData("route.bsu2_to_bucket.fragment_1.active")]
    [InlineData("equip.bucket.start")]
    public void Signal_mapping_line_and_card_register_address_enables_bit_input(string signalId)
    {
        using var fixture = new SignalMappingFixture(1200, 760);
        var rowGrid = SignalMappingRowGrid(fixture.View, signalId);
        var row = Assert.IsType<RouteMapSignalMappingRow>(rowGrid.DataContext);
        row.IsMapped = true;

        row.ClientPhysicalAddressText = "16420";
        Dispatcher.UIThread.RunJobs();

        var bitInput = rowGrid.Children
            .OfType<TextBox>()
            .Single(textBox => Grid.GetColumn(textBox) == 10);
        Assert.True(bitInput.IsEnabled);
    }

    [AvaloniaFact]
    public void Modbus_settings_data_map_rows_scroll_horizontally_without_overlap()
    {
        var options = new ModbusOptions
        {
            DataMap =
            [
                new ModbusDataPointOptions
                {
                    Name = "route.node.bsu_1.target.off",
                    Area = ModbusDataArea.Coil,
                    Address = 0,
                    Length = 1,
                    Access = ModbusDataAccess.Read,
                    Type = ModbusValueType.Bool,
                }
            ]
        };
        var viewModel = new ModbusSettingsDialogViewModel(
            "Настройки Modbus TCP Demo",
            ModbusOptions.DemoSectionName,
            options,
            new NullAppConfigService(),
            new ModbusDataMapValidator());
        var view = new ModbusSettingsDialogView { DataContext = viewModel };
        var window = new Window { Width = 900, Height = 520, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tabs = view.GetVisualDescendants().OfType<TabControl>().Single();
        tabs.SelectedIndex = 3;
        Dispatcher.UIThread.RunJobs();

        var row = view.GetVisualDescendants()
            .OfType<Grid>()
            .Single(item => item.Classes.Contains("modbus-data-point-row"));
        var controls = row.Children.OfType<Control>().ToArray();

        Assert.Equal(10, controls.Length);
        for (var index = 1; index < controls.Length; index++)
            Assert.True(controls[index - 1].Bounds.Right <= controls[index].Bounds.Left);

        Assert.Contains(
            view.GetVisualDescendants().OfType<ScrollViewer>(),
            scroll => scroll.Extent.Width > scroll.Viewport.Width);

        window.Close();
    }

    private static Grid FirstSignalMappingRowGrid(RouteMapSignalMappingView view)
        => view.GetVisualDescendants()
            .OfType<Grid>()
            .First(grid =>
                grid.DataContext is RouteMapSignalMappingRow
                && grid.Children.OfType<Control>().Any(control => Grid.GetColumn(control) == 13)
                && grid.Children.OfType<Control>().Any(control => Grid.GetColumn(control) == 14)
                && grid.Children.OfType<Grid>().Any(control => Grid.GetColumn(control) == 15));

    private static Grid SignalMappingRowGrid(RouteMapSignalMappingView view, string signalId)
        => view.GetVisualDescendants()
            .OfType<Grid>()
            .Single(grid =>
                grid.DataContext is RouteMapSignalMappingRow row
                && row.SignalId == signalId
                && grid.Children.OfType<Grid>().Any(control => Grid.GetColumn(control) == 15));

    private sealed class DialogFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "route-map-ui-tests", Guid.NewGuid().ToString("N"));
        private readonly RouteMapConfigurationManager _manager;
        private readonly RouteMapSettingsViewModel _viewModel;

        public DialogFixture(double width, double height)
        {
            Directory.CreateDirectory(_directory);
            var storage = new RouteMapConfigurationStorage(Path.Combine(_directory, "route-map.json"));
            var mapper = new RouteMapConfigurationMapper(RouteMapSeed.Create());
            _manager = new RouteMapConfigurationManager(
                storage,
                mapper,
                new RouteMapConfigurationValidator(),
                new RouteMapConfigurationMigrator());
            _viewModel = new RouteMapSettingsViewModel(_manager, storage, new NullFilePicker());
            Dialog = new RouteMapSettingsDialog { DataContext = _viewModel };
            Window = new Window { Width = width, Height = height, Content = Dialog };
            Window.Show();
            Dispatcher.UIThread.RunJobs();
        }

        public Window Window { get; }
        public RouteMapSettingsDialog Dialog { get; }

        public void SelectTab(int index)
        {
            var tabs = Dialog.GetVisualDescendants().OfType<TabControl>().Single();
            tabs.SelectedIndex = index;
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            Window.Close();
            _viewModel.Dispose();
            _manager.Dispose();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class SignalMappingFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "route-map-mapping-ui-tests", Guid.NewGuid().ToString("N"));
        private readonly RouteMapConfigurationManager _manager;
        private readonly RouteMapSignalMappingViewModel _viewModel;

        public SignalMappingFixture(double width, double height)
        {
            Directory.CreateDirectory(_directory);
            var storage = new RouteMapConfigurationStorage(Path.Combine(_directory, "route-map.json"));
            _manager = new RouteMapConfigurationManager(
                storage,
                new RouteMapConfigurationMapper(RouteMapSeed.Create()),
                new RouteMapConfigurationValidator(),
                new RouteMapConfigurationMigrator());
            var options = new ModbusOptions
            {
                Client = new ModbusEndpointOptions
                {
                    CoilStartAddress = 0,
                    CoilCount = 2000,
                    HoldingRegisterStartAddress = 16384,
                    RegisterCount = 100
                },
                Server = new ModbusEndpointOptions
                {
                    CoilStartAddress = 0,
                    CoilCount = 2000,
                    HoldingRegisterStartAddress = 16384,
                    RegisterCount = 100
                }
            };
            _viewModel = new RouteMapSignalMappingViewModel(
                _manager,
                new StaticOptionsMonitor(options),
                new NullAppConfigService(),
                new ModbusDataMapValidator(),
                new NoOpDataMapRuntime());
            View = new RouteMapSignalMappingView { DataContext = _viewModel };
            Window = new Window { Width = width, Height = height, Content = View };
            Window.Show();
            Dispatcher.UIThread.RunJobs();
        }

        public RouteMapSignalMappingView View { get; }
        public Window Window { get; }

        public void Dispose()
        {
            Window.Close();
            _viewModel.Dispose();
            _manager.Dispose();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class StaticOptionsMonitor(ModbusOptions value) : IOptionsMonitor<ModbusOptions>
    {
        public ModbusOptions CurrentValue { get; } = value;
        public ModbusOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<ModbusOptions, string?> listener) => null;
    }

    private sealed class NoOpDataMapRuntime : IModbusDataMapRuntime
    {
        public ModbusOperationResult ApplyDataMap(IReadOnlyList<ModbusDataPointOptions> dataMap) =>
            ModbusOperationResult.Success();
    }

    private sealed class NullAppConfigService : IAppConfigService
    {
        public T GetSection<T>(string sectionName) where T : class, new() => new();
        public string GetValue(string key) => string.Empty;
        public Task SaveSectionAsync<T>(string sectionName, T value, CancellationToken ct = default) => Task.CompletedTask;
        public void SaveUserSettings(UserSettings settings) { }
        public UserSettings LoadUserSettings() => new();
    }

    private sealed class NullFilePicker : IRouteMapSettingsFilePicker
    {
        public Task<string?> PickImportPathAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> PickExportPathAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private static WorkspaceViewModel CreateWorkspaceViewModel(bool isAdminMode) =>
        new(
            hostScreen: null!,
            authService: new TestAuthApp(),
            modbusDemo: null!,
            routeMapDashboard: null!,
            routeMapSignalMapping: null!,
            modbusRuntime: new NoOpModbusRuntime(),
            modbusOptions: new StaticModbusDemoOptionsProvider(),
            applicationOptions: Options.Create(new ApplicationOptions
            {
                WorkMode = isAdminMode ? ApplicationOptions.AdminWorkMode : ApplicationOptions.UserWorkMode
            }),
            logger: NullLogger<WorkspaceViewModel>.Instance);

    private sealed class TestAuthApp : IAuthApp
    {
        public bool IsAuthenticated { get; set; } = true;
        public bool Authenticate(string username, string password) => IsAuthenticated;
    }

    private sealed class StaticModbusDemoOptionsProvider : IModbusDemoOptionsProvider
    {
        public ModbusOptions CurrentValue { get; } = new();
    }

    private sealed class NoOpModbusRuntime : IModbusRuntimeService
    {
        public ModbusStatus Status => ModbusStatus.Stopped;
        public ModbusSnapshot ClientSnapshot => ModbusSnapshot.Empty;
        public ModbusSnapshot ServerSnapshot => ModbusSnapshot.Empty;
        public ModbusOptions CurrentOptions { get; } = new();
        public event EventHandler<ModbusStatus>? StatusChanged { add { } remove { } }
        public event EventHandler<ModbusSnapshot>? SnapshotChanged { add { } remove { } }
        public Task StartAsync(ModbusRunMode mode = ModbusRunMode.Both, ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestartAsync(ModbusRunMode mode = ModbusRunMode.Both, ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopClientAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopServerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
