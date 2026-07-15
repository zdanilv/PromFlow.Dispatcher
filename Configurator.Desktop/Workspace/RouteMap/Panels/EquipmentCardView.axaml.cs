using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ReactiveUI.Avalonia;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;

namespace Configurator.Desktop.Workspace.RouteMap.Panels;

public partial class EquipmentCardView : ReactiveUserControl<EquipmentCardViewModel>
{
    public EquipmentCardView()
    {
        AvaloniaXamlLoader.Load(this);

        AddHandler(
            InputElement.PointerPressedEvent,
            Button_PointerPressed,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        AddHandler(
            InputElement.PointerReleasedEvent,
            Button_PointerReleased,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        AddHandler(
            InputElement.PointerCaptureLostEvent,
            Button_PointerCaptureLost,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        AddHandler(
            InputElement.PointerEnteredEvent,
            Button_PointerEntered,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        AddHandler(
            InputElement.PointerExitedEvent,
            Button_PointerExited,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    private void Button_PointerPressed(object? sender, PointerPressedEventArgs e) =>
        SetPressed(e.Source, pressed: true);

    private void Button_PointerReleased(object? sender, PointerReleasedEventArgs e) =>
        SetPressed(e.Source, pressed: false);

    private void Button_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) =>
        SetPressed(e.Source, pressed: false);

    private void Button_PointerEntered(object? sender, PointerEventArgs e) =>
        SetHovered(e.Source, hovered: true);

    private void Button_PointerExited(object? sender, PointerEventArgs e)
    {
        SetPressed(e.Source, pressed: false);
        SetHovered(e.Source, hovered: false);
    }

    private void SetPressed(object? source, bool pressed)
    {
        if (ViewModel is null || FindNamedButton(source) is not { } buttonName)
            return;

        if (buttonName == "StartButton")
            ViewModel.IsStartPressed = pressed;
        else if (buttonName == "StopButton")
            ViewModel.IsStopPressed = pressed;
    }

    private void SetHovered(object? source, bool hovered)
    {
        if (ViewModel is null || FindNamedButton(source) != "StopButton")
            return;

        ViewModel.IsStopHovered = hovered;
    }

    private static string? FindNamedButton(object? source)
    {
        var control = source as Control;

        while (control is not null)
        {
            if (control.Name is "StartButton" or "StopButton")
                return control.Name;

            control = control.Parent as Control;
        }

        return null;
    }
}
