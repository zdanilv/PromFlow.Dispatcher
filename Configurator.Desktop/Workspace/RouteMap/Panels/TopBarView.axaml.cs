using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ReactiveUI.Avalonia;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;

namespace Configurator.Desktop.Workspace.RouteMap.Panels;

public partial class TopBarView : ReactiveUserControl<TopBarViewModel>
{
    public TopBarView()
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

    private void Button_PointerExited(object? sender, PointerEventArgs e)
    {
        if (e.Source is Control { Name: "AutomaticButton" or "ManualButton" or "ResetButton" or "EmergencyButton" })
            SetPressed(e.Source, pressed: false);
    }

    private void SetPressed(object? source, bool pressed)
    {
        if (ViewModel is null || FindNamedButton(source) is not { } buttonName)
            return;

        switch (buttonName)
        {
            case "AutomaticButton":
                ViewModel.SetAutomaticPressed(pressed);
                break;
            case "ManualButton":
                ViewModel.SetManualPressed(pressed);
                break;
            case "ResetButton":
                ViewModel.SetResetPressed(pressed);
                break;
            case "EmergencyButton":
                ViewModel.SetEmergencyPressed(pressed);
                break;
        }
    }

    private static string? FindNamedButton(object? source)
    {
        var control = source as Control;

        while (control is not null)
        {
            if (control.Name is "AutomaticButton" or "ManualButton" or "ResetButton" or "EmergencyButton")
                return control.Name;

            control = control.Parent as Control;
        }

        return null;
    }
}
