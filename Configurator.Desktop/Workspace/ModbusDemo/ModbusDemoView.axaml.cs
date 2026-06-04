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
    private readonly ModbusDemoRadioButtonToggleBehavior _radioButtonToggleBehavior = new();

    public ModbusDemoView()
    {
        InitializeComponent();

        // Q1 выглядит как RadioButton, но работает как удерживаемый переключатель.
        // Tunnel-перехват нужен до встроенной radio-логики Avalonia, иначе повторный клик оставит IsChecked = true.
        AddHandler(
            InputElement.PointerPressedEvent,
            RadioButtonToggle_PointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            InputElement.KeyDownEvent,
            RadioButtonToggle_KeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private void RadioButtonToggle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_radioButtonToggleBehavior.ToggleOffIfChecked(e.Source))
        {
            e.Handled = true;
        }
    }

    private void RadioButtonToggle_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Space or Key.Enter
            && _radioButtonToggleBehavior.ToggleOffIfChecked(e.Source))
        {
            e.Handled = true;
        }
    }
}

internal sealed class ModbusDemoRadioButtonToggleBehavior
{
    public bool ToggleOffIfChecked(object? source)
    {
        if (!TryGetRadioButtonToggle(source, out var radioButton, out var row)
            || !row.IsChecked)
        {
            return false;
        }

        // Avalonia RadioButton не снимает выбор при повторной активации. Перехватываем событие
        // до штатной radio-логики и сами чистим UI и ViewModel, чтобы в Modbus ушел 0.
        row.IsChecked = false;
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
