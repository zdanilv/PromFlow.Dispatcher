using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ReactiveUI.Avalonia;

namespace Configurator.Desktop.Workspace.ModbusDemo;

/// <summary>
/// Представление Avalonia для ModbusDemo.
/// </summary>
public partial class ModbusDemoView : ReactiveUserControl<ModbusDemoViewModel>
{
    private readonly ModbusDemoMomentaryCommandBehavior _momentaryCommandBehavior = new();
    private readonly ModbusDemoRadioButtonToggleBehavior _radioButtonToggleBehavior = new();

    public ModbusDemoView()
    {
        InitializeComponent();

        // MomentaryButton пишет 1 при нажатии и 0 при любом сценарии отпускания, включая потерю
        // pointer capture. Глобальная регистрация нужна, чтобы сработали и вложенные части контрола.
        AddHandler(
            InputElement.PointerPressedEvent,
            MomentaryCommand_PointerPressed,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        AddHandler(
            InputElement.PointerReleasedEvent,
            MomentaryCommand_PointerReleased,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        AddHandler(
            InputElement.PointerCaptureLostEvent,
            MomentaryCommand_PointerCaptureLost,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        AddHandler(
            InputElement.PointerExitedEvent,
            MomentaryCommand_PointerExited,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        AddHandler(
            InputElement.LostFocusEvent,
            MomentaryCommand_LostFocus,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    private async void MomentaryCommand_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (await _momentaryCommandBehavior.PressAsync(e.Source))
        {
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    private async void MomentaryCommand_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (await _momentaryCommandBehavior.ReleaseAsync())
        {
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private async void MomentaryCommand_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        await _momentaryCommandBehavior.ReleaseAsync();
    }

    private async void MomentaryCommand_PointerExited(object? sender, PointerEventArgs e)
    {
        await _momentaryCommandBehavior.ReleaseIfSourceAsync(e.Source);
    }

    private async void MomentaryCommand_LostFocus(object? sender, RoutedEventArgs e)
    {
        await _momentaryCommandBehavior.ReleaseIfSourceAsync(e.Source);
    }

    private void RadioButtonToggle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _radioButtonToggleBehavior.CaptureIfChecked(e.Source);
    }

    private void RadioButtonToggle_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Space or Key.Enter)
        {
            _radioButtonToggleBehavior.CaptureIfChecked(e.Source);
        }
    }

    private void RadioButtonToggle_Click(object? sender, RoutedEventArgs e)
    {
        if (_radioButtonToggleBehavior.ToggleOffIfCaptured(e.Source))
        {
            e.Handled = true;
        }
    }
}

internal sealed class ModbusDemoMomentaryCommandBehavior
{
    private ModbusCommandBitRow? _activeRow;

    public async Task<bool> PressAsync(object? source)
    {
        if (!TryGetCommandRow(source, out var row) || !row.IsPulseControl)
        {
            return false;
        }

        if (_activeRow is not null && !ReferenceEquals(_activeRow, row))
        {
            await ReleaseAsync();
        }

        if (_activeRow is null)
        {
            _activeRow = row;
            await row.PressAsync();
        }

        return true;
    }

    public async Task<bool> ReleaseAsync()
    {
        if (_activeRow is null)
        {
            return false;
        }

        var row = _activeRow;
        _activeRow = null;
        await row.ReleaseAsync();
        return true;
    }

    public async Task<bool> ReleaseIfSourceAsync(object? source)
    {
        if (_activeRow is null
            || !TryGetCommandRow(source, out var sourceRow)
            || !ReferenceEquals(_activeRow, sourceRow))
        {
            return false;
        }

        return await ReleaseAsync();
    }

    internal static bool TryGetCommandRow(object? source, out ModbusCommandBitRow row)
    {
        if (source is Control { DataContext: ModbusCommandBitRow commandRow })
        {
            row = commandRow;
            return true;
        }

        row = null!;
        return false;
    }
}

internal sealed class ModbusDemoRadioButtonToggleBehavior
{
    private ModbusCommandBitRow? _checkedRowBeforeActivation;

    public bool CaptureIfChecked(object? source)
    {
        _checkedRowBeforeActivation = null;

        if (!TryGetRadioButtonToggle(source, out _, out var row) || !row.IsChecked)
        {
            return false;
        }

        _checkedRowBeforeActivation = row;
        return true;
    }

    public bool ToggleOffIfCaptured(object? source)
    {
        var capturedRow = _checkedRowBeforeActivation;
        _checkedRowBeforeActivation = null;

        if (capturedRow is null
            || !TryGetRadioButtonToggle(source, out var radioButton, out var sourceRow)
            || !ReferenceEquals(capturedRow, sourceRow))
        {
            return false;
        }

        // Avalonia RadioButton не снимает выбор при повторной активации. Q1 намеренно остается
        // radio-визуалом, но пишет удерживаемый бит, поэтому чистим и UI, и состояние ViewModel.
        sourceRow.IsChecked = false;
        radioButton.IsChecked = false;
        return true;
    }

    private static bool TryGetRadioButtonToggle(
        object? source,
        out RadioButton radioButton,
        out ModbusCommandBitRow row)
    {
        for (var control = source as Control; control is not null; control = control.Parent as Control)
        {
            if (control is RadioButton { DataContext: ModbusCommandBitRow commandRow } button
                && commandRow.IsRadioButtonToggle)
            {
                radioButton = button;
                row = commandRow;
                return true;
            }
        }

        radioButton = null!;
        row = null!;
        return false;
    }
}
