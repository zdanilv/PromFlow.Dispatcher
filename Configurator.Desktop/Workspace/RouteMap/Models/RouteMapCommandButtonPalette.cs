namespace Configurator.Desktop.Workspace.RouteMap.Models;

internal sealed record RouteMapCommandButtonColors(
    string NormalBackground,
    string HoverBackground,
    string PressedBackground,
    string CheckedBackground,
    string NormalForeground,
    string PressedForeground,
    string CheckedForeground)
{
    public string Background(bool isPressed, bool isChecked, bool isHovered = false) =>
        isPressed ? PressedBackground :
        isChecked ? CheckedBackground :
        isHovered ? HoverBackground :
        NormalBackground;

    public string Foreground(bool isPressed, bool isChecked) =>
        isPressed ? PressedForeground :
        isChecked ? CheckedForeground :
        NormalForeground;
}

internal static class RouteMapCommandButtonPalette
{
    public static RouteMapCommandButtonColors Mode { get; } = new(
        NormalBackground: "#ECEFF1",
        HoverBackground: "#ECEFF1",
        PressedBackground: "#8AB5FF",
        CheckedBackground: "#003CA3",
        NormalForeground: "#59636E",
        PressedForeground: "#FFFFFF",
        CheckedForeground: "#FFFFFF");

    public static RouteMapCommandButtonColors Start { get; } = new(
        NormalBackground: "#D0D0D0",
        HoverBackground: "#8AFF8E",
        PressedBackground: "#00D107",
        CheckedBackground: "#00D107",
        NormalForeground: "#101820",
        PressedForeground: "#101820",
        CheckedForeground: "#FFFFFF");

    public static RouteMapCommandButtonColors Selector { get; } = new(
        NormalBackground: "#D0D0D0",
        HoverBackground: "#D0D0D0",
        PressedBackground: "#8AB5FF",
        CheckedBackground: "#003CA3",
        NormalForeground: "#101820",
        PressedForeground: "#101820",
        CheckedForeground: "#FFFFFF");

    public static RouteMapCommandButtonColors Reset { get; } = new(
        NormalBackground: "#ECEFF1",
        HoverBackground: "#FFF78A",
        PressedBackground: "#D1C300",
        CheckedBackground: "#D1C300",
        NormalForeground: "#101820",
        PressedForeground: "#FFFFFF",
        CheckedForeground: "#101820");

    public static RouteMapCommandButtonColors Emergency { get; } = new(
        NormalBackground: "#FF2626",
        HoverBackground: "#FF8A8A",
        PressedBackground: "#D10000",
        CheckedBackground: "#D10000",
        NormalForeground: "#101820",
        PressedForeground: "#FFFFFF",
        CheckedForeground: "#FFFFFF");
}
