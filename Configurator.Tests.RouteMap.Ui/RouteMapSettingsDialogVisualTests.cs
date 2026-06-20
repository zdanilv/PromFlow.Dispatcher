using Avalonia.Controls;
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
using Configurator.Desktop.Dialogs.ModbusSettingsDialog;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Settings;
using Configurator.Desktop.Workspace.RouteMap.SignalMapping;
using Configurator.Desktop.Workspace;
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
    [InlineData(3, "Положение подписи")]
    [InlineData(3, "Цвет сигнального контура")]
    [InlineData(3, "Толщина сигнального контура")]
    [InlineData(4, "Зазор от узлов")]
    [InlineData(4, "Края линии")]
    [InlineData(5, "Тип ПУСК")]
    [InlineData(5, "Тип СТОП")]
    [InlineData(2, "Тип кнопки")]
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
                    Role = SignalBindingRole.State,
                    SignalId = "node.state",
                    Direction = SignalBindingDirection.Read,
                    ValueType = Configurator.Application.Services.Signals.SignalValueType.String,
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
        var view = new WorkspaceView();
        var window = new Window { Width = 1200, Height = 760, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tabs = view.GetVisualDescendants().OfType<TabControl>().Single();
        var headers = tabs.Items.Cast<TabItem>().Select(item => item.Header?.ToString() ?? string.Empty).ToArray();

        Assert.Equal(["Route Map", "SignalId ↔ Modbus", "Modbus Demo"], headers);

        window.Close();
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
                Client = new ModbusEndpointOptions { CoilCount = 100, RegisterCount = 100 },
                Server = new ModbusEndpointOptions { CoilCount = 100, RegisterCount = 100 }
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
}
