using Configurator.Desktop.Workspace.ModbusDemo;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Windows.Input;
using Xunit;
using static Configurator.Infrastructure.Modbus.Tests.ModbusDemoTestContext;
using FakeDialogService = Configurator.Infrastructure.Modbus.Tests.ModbusDemoTestContext.FakeDialogService;
using FakeModbusTcpService = Configurator.Infrastructure.Modbus.Tests.ModbusDemoTestContext.FakeModbusTcpService;

namespace Configurator.Infrastructure.Modbus.Tests.UI;

public sealed class ModbusDemoViewUiTests
{
    [AvaloniaFact]
    public void ModbusDemoView_RendersCommandControls()
    {
        using var harness = new ModbusDemoUiHarness();

        Assert.NotNull(harness.FindCommandControl<RadioButton>("Commands_1", 0));
        Assert.NotNull(harness.FindCommandControl<CheckBox>("Commands_1", 1));
        Assert.NotNull(harness.FindCommandControl<ToggleButton>("Commands_1", 2));
        Assert.NotNull(harness.FindCommandControl<ToggleButton>("Commands_1", 3));
        Assert.NotNull(harness.FindCommandControl<ToggleButton>("Commands_1", 4));
        Assert.NotNull(harness.FindCommandControl<ToggleButton>("Commands_1", 5));
        Assert.NotNull(harness.FindCommandControl<ToggleButton>("Commands_3", 0));
        Assert.NotNull(harness.FindCommandControl<ToggleButton>("Commands_4", 0));
        Assert.NotNull(harness.FindCommandControl<ToggleButton>("Commands_4", 1));
    }

    [AvaloniaFact]
    public async Task RadioButtonQ1_ClickTwice_WritesOneThenZero()
    {
        using var harness = new ModbusDemoUiHarness();
        var radioButton = harness.FindCommandControl<RadioButton>("Commands_1", 0);

        harness.Click(radioButton);
        Assert.True(await WaitForAsync(() => harness.Service.SetCalls.Count == 1));
        Assert.True(radioButton.IsChecked);

        harness.Click(radioButton);
        Assert.True(await WaitForAsync(() => harness.Service.SetCalls.Count == 2));

        Assert.False(radioButton.IsChecked);
        Assert.Equal(("Commands_1", (ushort)1), harness.Service.SetCalls[0]);
        Assert.Equal(("Commands_1", (ushort)0), harness.Service.SetCalls[1]);
    }

    [AvaloniaFact]
    public async Task ToggleCommand_ClickTwice_WritesHeldState()
    {
        using var harness = new ModbusDemoUiHarness();
        var toggleButton = harness.FindCommandControl<ToggleButton>("Commands_1", 2);

        harness.Click(toggleButton);
        Assert.True(await WaitForAsync(() => harness.Service.SetCalls.Count == 1));
        Assert.True(toggleButton.IsChecked);

        harness.Click(toggleButton);
        Assert.True(await WaitForAsync(() => harness.Service.SetCalls.Count == 2));

        Assert.False(toggleButton.IsChecked);
        Assert.Equal(("Commands_1", (ushort)4), harness.Service.SetCalls[0]);
        Assert.Equal(("Commands_1", (ushort)0), harness.Service.SetCalls[1]);
    }

    [AvaloniaFact]
    public async Task Commands4Toggle_ClickTwice_WritesHeldState()
    {
        using var harness = new ModbusDemoUiHarness();
        var toggleButton = harness.FindCommandControl<ToggleButton>("Commands_4", 0);

        harness.Click(toggleButton);
        Assert.True(await WaitForAsync(() => harness.Service.SetCalls.Count == 1));
        Assert.True(toggleButton.IsChecked);

        harness.Click(toggleButton);
        Assert.True(await WaitForAsync(() => harness.Service.SetCalls.Count == 2));

        Assert.False(toggleButton.IsChecked);
        Assert.Equal(("Commands_4", (ushort)1), harness.Service.SetCalls[0]);
        Assert.Equal(("Commands_4", (ushort)0), harness.Service.SetCalls[1]);
    }

    [AvaloniaFact]
    public async Task TelemetryPublish_UpdatesVisibleBitRows()
    {
        using var harness = new ModbusDemoUiHarness();
        var telemetryBit = harness.ViewModel.TelemetryGroups
            .Single(group => group.PointName == "Telemetry_1")
            .Bits.Single(row => row.BitIndex == 0);

        harness.Service.Publish("Telemetry_1", (ushort)1);
        harness.DrainUi();

        Assert.True(await WaitForAsync(() => telemetryBit.ValueText == "1"));
        var indicator = harness.FindRedIndicator(telemetryBit);
        var valueText = harness.FindTextBlock(telemetryBit, "1");

        Assert.True(indicator.IsVisible);
        Assert.Equal("1", valueText.Text);
    }

    [AvaloniaFact]
    public async Task ParameterEdit_WriteButton_WritesEditedValue()
    {
        using var harness = new ModbusDemoUiHarness();
        var parameter = harness.ViewModel.ParameterRows.Single(row => row.PointName == "MB_Hz");
        var textBox = harness.FindParameterTextBox(parameter.PointName);
        var writeButton = harness.FindRegularButton(harness.ViewModel.WriteParametersCommand);

        textBox.Focus();
        textBox.Text = "42";
        harness.DrainUi();
        Assert.Equal("42", parameter.EditValueText);

        harness.Click(writeButton);
        Assert.True(await WaitForAsync(() => harness.Service.SetCalls.Any(call => call == ("MB_Hz", (ushort)42))));
    }

    private sealed class ModbusDemoUiHarness : IDisposable
    {
        public ModbusDemoUiHarness()
        {
            Service = new FakeModbusTcpService();
            Dialog = new FakeDialogService();
            ViewModel = CreateViewModel(Service, Dialog);
            View = new ModbusDemoView
            {
                DataContext = ViewModel
            };
            Window = new Window
            {
                Width = 1280,
                Height = 900,
                Content = View
            };
            Window.Show();
            DrainUi();
        }

        public FakeModbusTcpService Service { get; }

        public FakeDialogService Dialog { get; }

        public ModbusDemoViewModel ViewModel { get; }

        public ModbusDemoView View { get; }

        public Window Window { get; }

        public T FindCommandControl<T>(string pointName, int bitIndex)
            where T : ToggleButton
            => Window
                .GetVisualDescendants()
                .OfType<T>()
                .Single(control =>
                    control.GetType() == typeof(T)
                    && control.IsVisible
                    && control.DataContext is ModbusCommandBitRow row
                    && row.PointName == pointName
                    && row.BitIndex == bitIndex);

        public TextBox FindParameterTextBox(string pointName)
            => Window
                .GetVisualDescendants()
                .OfType<TextBox>()
                .Single(control =>
                    control.IsVisible
                    && control.DataContext is ModbusParameterRow row
                    && row.PointName == pointName);

        public Button FindRegularButton(ICommand command)
            => Window
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(control =>
                    control.GetType() == typeof(Button)
                    && ReferenceEquals(control.Command, command));

        public Border FindRedIndicator(ModbusTelemetryBitRow row)
            => Window
                .GetVisualDescendants()
                .OfType<Border>()
                .Single(control =>
                    control.DataContext == row
                    && control.Width == 10
                    && control.Height == 10);

        public TextBlock FindTextBlock(object dataContext, string text)
            => Window
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(control => control.DataContext == dataContext && control.Text == text);

        public void Click(Control control)
        {
            DrainUi();
            var point = control.TranslatePoint(
                new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
                Window);

            Assert.True(point.HasValue, $"Control {control.GetType().Name} is not attached to the test window.");
            Window.MouseDown(point.Value, MouseButton.Left, RawInputModifiers.None);
            Window.MouseUp(point.Value, MouseButton.Left, RawInputModifiers.None);
            DrainUi();
        }

        public void DrainUi()
        {
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                DisposeOnUiThread();
            }
            else
            {
                Dispatcher.UIThread.Invoke(DisposeOnUiThread);
            }
        }

        private void DisposeOnUiThread()
        {
            Window.Close();
            ViewModel.Dispose();
        }
    }
}
