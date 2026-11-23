using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Dialogs.HelpDialog;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Panels;
using Configurator.Desktop.Workspace.RouteMap.Settings;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;
using Xunit;

namespace Configurator.Tests.RouteMap.Ui;

public sealed class TopBarViewTests
{
    [AvaloniaTheory]
    [InlineData(1024, 1.0)]
    [InlineData(1200, 1.0)]
    [InlineData(1366, 1.0)]
    [InlineData(1920, 1.0)]
    [InlineData(1024, 1.5)]
    public void TopBar_keeps_brand_and_help_fixed_and_every_command_reachable(int width, double scaling)
    {
        using var fixture = new TopBarFixture(width, scaling);
        var brand = fixture.View.FindControl<StackPanel>("BrandPanel")!;
        var panel = Assert.IsType<StackPanel>(fixture.Scroll.Content);
        Assert.Equal(new[] { "ПРОМ", "Диспетчер" },
            brand.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
        Assert.Equal("ПОМОЩЬ", fixture.Help.Content);
        Assert.Equal(fixture.AdminButtons.Concat(new[] { fixture.Reset, fixture.Emergency, fixture.Settings }),
            panel.Children.Cast<Button>());
        Assert.All(fixture.AdminButtons, button => Assert.IsType<Button>(button));
        Assert.All(panel.Children, button => Assert.True(button.IsEffectivelyVisible));
        Assert.Equal(new[] { "ВЫГРУЗКА", "ВРАЩЕНИЕ", "ЗАГРУЗКА" },
            fixture.AdminButtons.Select(button => Assert.IsType<string>(button.Content)));
        Assert.False(fixture.View.FindControl<ToggleButton>("AutomaticButton")!.IsEffectivelyVisible);
        Assert.False(fixture.View.FindControl<ToggleButton>("ManualButton")!.IsEffectivelyVisible);

        var brandBounds = fixture.BoundsOf(brand);
        var helpBounds = fixture.BoundsOf(fixture.Help);
        fixture.Capture($"topbar-admin-{width}-{scaling:0.0}");
        CheckLayout();
        if (width == 1920)
            Assert.Equal(0, fixture.Scroll.ScrollBarMaximum.X);

        fixture.ViewModel.ApplySettings(fixture.SettingsModel with
        {
            Unload = fixture.SettingsModel.Unload with { Text = "ВЫГРУЗКА МАТЕРИАЛА" },
            Rotation = fixture.SettingsModel.Rotation with { Text = "ВРАЩЕНИЕ МЕХАНИЗМА" },
            Load = fixture.SettingsModel.Load with { Text = "ЗАГРУЗКА МАТЕРИАЛА" },
        });
        fixture.Layout();
        if (width == 1024)
            Assert.True(fixture.Scroll.ScrollBarMaximum.X > 0);
        CheckLayout();
        fixture.Scroll.Offset = default;
        fixture.Layout();
        fixture.Capture($"topbar-admin-long-{width}-{scaling:0.0}");

        fixture.SetMode(admin: false);
        Assert.All(fixture.AdminButtons, button =>
        {
            Assert.False(button.IsEffectivelyVisible);
            Assert.False(button.Focus());
        });
        Assert.False(fixture.Settings.IsEffectivelyVisible);
        Assert.True(fixture.Help.IsEffectivelyEnabled);
        if (scaling == 1.0)
            Assert.Equal(0, fixture.Scroll.ScrollBarMaximum.X);
        Assert.Equal(panel.Spacing, fixture.Emergency.Bounds.Left - fixture.Reset.Bounds.Right, precision: 0);
        CheckLayout();
        fixture.Capture($"topbar-user-{width}-{scaling:0.0}");

        fixture.SetMode(admin: true);
        Assert.All(panel.Children, button => Assert.True(button.IsEffectivelyVisible));

        void CheckLayout()
        {
            var viewport = fixture.Scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().Single();
            var viewportBounds = fixture.BoundsOf(viewport);
            Assert.Equal(64, fixture.View.Bounds.Height);
            Assert.Equal(brandBounds.X, fixture.BoundsOf(brand).X);
            Assert.Equal(helpBounds.X, fixture.BoundsOf(fixture.Help).X);
            Assert.True(brandBounds.Right <= viewportBounds.Left);
            Assert.True(viewportBounds.Right <= helpBounds.Left);

            var right = 0d;
            foreach (var button in panel.Children.OfType<Button>().Where(button => button.IsEffectivelyVisible))
            {
                Assert.True(right <= button.Bounds.Left);
                right = button.Bounds.Right;
                button.BringIntoView();
                fixture.Layout();
                var bounds = fixture.BoundsOf(button);
                if (bounds.Width <= viewportBounds.Width)
                {
                    Assert.True(bounds.Left >= viewportBounds.Left - 1 && bounds.Right <= viewportBounds.Right + 1,
                        $"{button.Content} at {bounds} is outside viewport {viewportBounds}.");
                }
                else
                {
                    Assert.True(bounds.Left <= viewportBounds.Left + 1 && bounds.Right >= viewportBounds.Right - 1);
                    Assert.Equal(button.Content, AutomationProperties.GetName(button));
                    Assert.Equal(button.Content, ToolTip.GetTip(button));
                }
                Assert.True(bounds.Top >= viewportBounds.Top - 1 && bounds.Bottom <= viewportBounds.Bottom + 1);
                var label = button.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => Equals(button.Content, text.Text));
                Assert.True(label.DesiredSize.Width <= label.Bounds.Width + 1);
            }
        }
    }

    [AvaloniaFact]
    public void TopBar_tabs_through_commands_and_scrolls_focused_button_into_view()
    {
        using var fixture = new TopBarFixture(1024);
        fixture.ViewModel.ApplySettings(fixture.SettingsModel with
        {
            Load = fixture.SettingsModel.Load with { Text = "ЗАГРУЗКА МАТЕРИАЛА В ПРИЁМНЫЙ БУНКЕР" },
        });
        fixture.Layout();
        Assert.False(fixture.BoundsOf(fixture.Scroll).Contains(fixture.BoundsOf(fixture.Settings)));
        var focus = fixture.Window.FocusManager!;
        Assert.True(fixture.Help.Focus(NavigationMethod.Tab));
        fixture.Press(Key.Tab);
        Assert.Same(fixture.AdminButtons[0], focus.GetFocusedElement());
        Assert.Equal(Color.Parse("#003CA3"), BrushColor(fixture.AdminButtons[0], p => p.BorderBrush));
        fixture.Press(Key.Tab);
        Assert.Same(fixture.AdminButtons[1], focus.GetFocusedElement());
        foreach (var button in new[] { fixture.AdminButtons[2], fixture.Reset, fixture.Emergency, fixture.Settings, fixture.Help })
        {
            fixture.Press(Key.Tab);
            Assert.Same(button, focus.GetFocusedElement());
            if (button != fixture.Help)
                Assert.True(fixture.BoundsOf(fixture.Scroll).Right + 1 >= fixture.BoundsOf(button).Right);
        }
        Assert.True(fixture.Scroll.Offset.X > 0);
        Assert.True(fixture.AdminButtons[0].Focus(NavigationMethod.Tab));
        fixture.Press(Key.Enter);
        Assert.True(fixture.AdminButtons[1].Focus(NavigationMethod.Tab));
        fixture.Press(Key.Space);
        Assert.Collection(fixture.CommandDispatcher.Requests,
            request => AssertRequest(request, "system.command.unload", ModbusWriteMode.Pulse),
            request => AssertRequest(request, "system.command.rotate", ModbusWriteMode.Pulse));

        fixture.SetMode(admin: false);
        Assert.True(fixture.Help.Focus(NavigationMethod.Tab));
        fixture.Press(Key.Tab, RawInputModifiers.Shift);
        Assert.Same(fixture.Emergency, focus.GetFocusedElement());
        fixture.Press(Key.Tab, RawInputModifiers.Shift);
        Assert.Same(fixture.Reset, focus.GetFocusedElement());
        fixture.Press(Key.Tab);
        Assert.Same(fixture.Emergency, focus.GetFocusedElement());
        fixture.Press(Key.Tab);
        Assert.Same(fixture.Help, focus.GetFocusedElement());
        Assert.Equal(2, fixture.CommandDispatcher.Requests.Count);
    }

    [AvaloniaFact]
    public async Task TopBar_pointer_states_dispatch_once_and_busy_recovers_after_error()
    {
        using var fixture = new TopBarFixture(1366);
        var (unload, rotation, load) = (fixture.AdminButtons[0], fixture.AdminButtons[1], fixture.AdminButtons[2]);
        Assert.Equal(Color.Parse("#ECEFF1"), Background(unload));
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.CommandDispatcher.Completion = pending.Task;
        var point = fixture.CenterOf(unload);
        fixture.Window.MouseMove(point);
        Assert.Equal(Color.Parse("#DCE9FB"), Background(unload));
        fixture.Window.MouseDown(point, MouseButton.Left);
        Assert.Equal(Color.Parse("#A9CAFA"), Background(unload));
        Assert.Empty(fixture.CommandDispatcher.Requests);
        fixture.Window.MouseUp(point, MouseButton.Left);
        fixture.Layout();
        AssertRequest(Assert.Single(fixture.CommandDispatcher.Requests), "system.command.unload", ModbusWriteMode.Pulse);
        Assert.False(unload.IsEffectivelyEnabled);
        Assert.True(rotation.IsEffectivelyEnabled);
        Assert.True(load.IsEffectivelyEnabled);
        Assert.Equal(Color.Parse("#C5DDFB"), Background(unload));
        Assert.Equal("Отправка команды", AutomationProperties.GetItemStatus(unload));
        fixture.Capture("topbar-admin-busy");
        fixture.Click(unload);
        Assert.False(unload.Focus());
        Assert.Single(fixture.CommandDispatcher.Requests);
        fixture.CommandDispatcher.Completion = Task.CompletedTask;
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        unload.PropertyChanged += (_, args) =>
        {
            if (args.Property == InputElement.IsEffectivelyEnabledProperty && unload.IsEffectivelyEnabled)
                recovered.TrySetResult();
        };
        pending.SetException(new TimeoutException("Test timeout"));
        await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Layout();
        Assert.True(unload.IsEffectivelyEnabled);
        Assert.Null(AutomationProperties.GetItemStatus(unload));
        Assert.Contains("Test timeout", Assert.Single(fixture.Errors));

        fixture.ViewModel.ApplyAdminCommandRuntime(RouteMapRuntimeState.Empty with
        {
            UnloadCommand = new(false, false),
            RotationCommand = new(true, true),
            LoadCommand = new(true, null),
        });
        Assert.False(unload.IsEffectivelyEnabled);
        Assert.Equal(Color.Parse("#E2E6E9"), Background(unload));
        fixture.Capture("topbar-admin-disabled");
        fixture.Click(unload);
        Assert.False(unload.Focus());
        Assert.True(fixture.Help.Focus(NavigationMethod.Tab));
        fixture.Press(Key.Tab);
        Assert.Same(rotation, fixture.Window.FocusManager!.GetFocusedElement());
        Assert.Single(fixture.CommandDispatcher.Requests);
        fixture.Click(rotation);
        fixture.Click(load);
        Assert.Collection(fixture.CommandDispatcher.Requests,
            request => AssertRequest(request, "system.command.unload", ModbusWriteMode.Pulse),
            request => AssertRequest(request, "system.command.rotate", ModbusWriteMode.Pulse),
            request => AssertRequest(request, "system.command.load", ModbusWriteMode.Pulse));
    }

    [AvaloniaFact]
    public void TopBar_help_and_reset_show_their_palettes_through_pointer_states()
    {
        using var fixture = new TopBarFixture(1366);
        CheckPointerColors(fixture.Help, "#003CA3", "#0A4DB5", "#002F80", "#FFFFFF");
        CheckPointerColors(fixture.Reset, "#FFE34D", "#FFF78A", "#D1C300", "#101820");

        fixture.ViewModel.ApplyRuntime(false, true, true, false, true);
        fixture.Layout();
        Assert.True(fixture.Reset.IsChecked);
        AssertColors(fixture.Reset, "#D1C300", "#101820");
        fixture.Window.MouseMove(fixture.CenterOf(fixture.Reset));
        AssertColors(fixture.Reset, "#D1C300", "#101820");

        void CheckPointerColors(Button button, string normal, string hover, string pressed, string foreground)
        {
            AssertColors(button, normal, foreground);
            var point = fixture.CenterOf(button);
            fixture.Window.MouseMove(point);
            AssertColors(button, hover, foreground);
            fixture.Window.MouseDown(point, MouseButton.Left);
            AssertColors(button, pressed, foreground);
            fixture.Window.MouseUp(point, MouseButton.Left);
            AssertColors(button, hover, foreground);
            fixture.Window.MouseMove(new Point(1, 1));
            AssertColors(button, normal, foreground);
        }

        static void AssertColors(Button button, string background, string foreground)
        {
            Assert.Equal(Color.Parse(background), Background(button));
            Assert.Equal(Color.Parse(foreground), BrushColor(button, presenter => presenter.Foreground));
        }
    }

    [AvaloniaFact]
    public void TopBar_existing_buttons_keep_their_commands()
    {
        using var fixture = new TopBarFixture(1366);
        fixture.Click(fixture.Reset);
        fixture.Click(fixture.Emergency);
        fixture.Click(fixture.Settings);
        fixture.Click(fixture.Help);
        Assert.Collection(fixture.CommandDispatcher.Requests,
            request => AssertRequest(request, "system.reset", ModbusWriteMode.Pulse),
            request => AssertRequest(request, "system.emergency", null));
        Assert.Equal(1, fixture.Dialogs.SettingsOpened);
        Assert.Equal(1, fixture.Dialogs.HelpOpened);
    }

    private static void AssertRequest(SignalWriteRequest request, string signalId, ModbusWriteMode? writeMode) =>
        Assert.Equal((signalId, true, writeMode), (request.SignalId, Assert.IsType<bool>(request.Value), request.RequiredWriteMode));

    private static Color Background(Button button) => BrushColor(button, presenter => presenter.Background);

    private static Color BrushColor(Button button, Func<ContentPresenter, IBrush?> selectBrush)
    {
        var presenter = button.GetVisualDescendants().OfType<ContentPresenter>()
            .Single(control => control.Name == "PART_ContentPresenter");
        return Assert.IsAssignableFrom<ISolidColorBrush>(selectBrush(presenter)).Color;
    }

    private sealed class TopBarFixture : IDisposable
    {
        public TopBarFixture(int width, double scaling = 1.0)
        {
            Window = new Window { Width = width / scaling, Height = 120, Content = View };
            Window.SetRenderScaling(scaling);
            SetMode(admin: true);
            Window.Show();
            Layout();
        }

        public TopBarView View { get; } = new() { Height = 64 };
        public Window Window { get; }
        public TopBarViewModel ViewModel => Assert.IsType<TopBarViewModel>(View.DataContext);
        public RouteTopBarSettings SettingsModel { get; } = RouteMapSeed.Create().TopBar!;
        public RecordingDispatcher CommandDispatcher { get; } = new();
        public RecordingDialogs Dialogs { get; } = new();
        public List<string> Errors { get; } = [];
        public ScrollViewer Scroll => View.FindControl<ScrollViewer>("CommandScroll")!;
        public ToggleButton Reset => View.FindControl<ToggleButton>("ResetButton")!;
        public ToggleButton Emergency => View.FindControl<ToggleButton>("EmergencyButton")!;
        public Button Settings => View.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("settings"));
        public Button Help => View.FindControl<Button>("HelpButton")!;
        public Button[] AdminButtons =>
        [
            View.FindControl<Button>("UnloadButton")!,
            View.FindControl<Button>("RotationButton")!,
            View.FindControl<Button>("LoadButton")!,
        ];

        public void SetMode(bool admin)
        {
            var viewModel = new TopBarViewModel(admin, Dialogs, CommandDispatcher, SettingsModel,
                isSettingsVisible: admin, helpDialogService: Dialogs, commandErrorHandler: Errors.Add);
            viewModel.ApplyRuntime(false, true, false, false, true);
            viewModel.ApplyAdminCommandRuntime(RouteMapRuntimeState.Empty with
            {
                UnloadCommand = new(true, null),
                RotationCommand = new(true, null),
                LoadCommand = new(true, null),
            });
            View.DataContext = viewModel;
            Layout();
        }

        public void Layout()
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
        }

        public Rect BoundsOf(Control control) => new(control.TranslatePoint(default, Window)!.Value, control.Bounds.Size);

        public Point CenterOf(Button button) => BoundsOf(button).Center;

        public void Click(Button button)
        {
            var point = CenterOf(button);
            Window.MouseMove(point);
            Window.MouseDown(point, MouseButton.Left);
            Window.MouseUp(point, MouseButton.Left);
            Layout();
        }

        public void Press(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Window.KeyPress(key, modifiers, PhysicalKey.None, null);
            Window.KeyRelease(key, modifiers, PhysicalKey.None, null);
            Layout();
        }

        public void Capture(string name)
        {
            if (HeadlessTestApplication.ScreenshotDirectory is not { Length: > 0 } directory)
                return;

            Directory.CreateDirectory(directory);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var bitmap = Assert.IsType<Avalonia.Media.Imaging.WriteableBitmap>(Window.CaptureRenderedFrame());
            bitmap.Save(Path.Combine(directory, name + ".png"));
        }

        public void Dispose() => Window.Close();
    }

    private sealed class RecordingDispatcher : IEquipmentCommandDispatcher
    {
        public List<SignalWriteRequest> Requests { get; } = [];
        public Task Completion { get; set; } = Task.CompletedTask;

        public Task DispatchAsync(SignalWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Completion;
        }
    }

    private sealed class RecordingDialogs : IRouteMapSettingsDialogService, IHelpDialogService
    {
        public int SettingsOpened { get; private set; }
        public int HelpOpened { get; private set; }

        Task IRouteMapSettingsDialogService.ShowAsync(CancellationToken cancellationToken)
        {
            SettingsOpened++;
            return Task.CompletedTask;
        }

        Task IHelpDialogService.ShowAsync(CancellationToken cancellationToken)
        {
            HelpOpened++;
            return Task.CompletedTask;
        }
    }
}
