namespace Configurator.Desktop.Workspace.RouteMap.Models;

public enum RouteNodeKind
{
    Default,
    Switch,
    Mixer,
    Conveyor,
    Sensor,
    ServicePoint
}

public enum RouteNodeMenuKind
{
    None,
    SendOnly,
    SendAndReturn
}

public enum RouteNodeLabelPlacement
{
    Below,
    Right
}

public enum RouteSegmentKind
{
    Straight,
    RoundedElbow90
}

public enum RouteElbowOrder
{
    VerticalThenHorizontal,
    HorizontalThenVertical
}

public enum RouteLineCap
{
    Flat,
    Round
}

public enum RouteCardVerticalAnchorKind
{
    ChainBoundsCenter,
    Node
}

public enum RoutePlaceholderPlacement
{
    Above,
    Below,
    Both
}

public enum RoutePlaceholderHeightMode
{
    MatchCard,
    Fixed
}

public enum RouteCommandButtonKind
{
    Toggle,
    Momentary
}

public sealed record RouteThickness(
    double Left,
    double Top,
    double Right,
    double Bottom)
{
    public static RouteThickness Uniform(double value) => new(value, value, value, value);
}

public sealed record RouteCornerRadius(
    double TopLeft,
    double TopRight,
    double BottomRight,
    double BottomLeft)
{
    public static RouteCornerRadius Uniform(double value) => new(value, value, value, value);
}

public sealed record RouteMapPaletteSettings
{
    public string Background { get; init; } = "#F7F8F8";
    public string Text { get; init; } = "#44505C";
    public string MutedText { get; init; } = "#77828D";
    public string Track { get; init; } = "#C9CED0";
    public string ActiveTrack { get; init; } = "#2D56B3";
    public string Ready { get; init; } = "#3A9D5D";
    public string Running { get; init; } = "#2563EB";
    public string Warning { get; init; } = "#D99B22";
    public string Fault { get; init; } = "#D95D4E";
    public string Offline { get; init; } = "#68717A";
    public string Disabled { get; init; } = "#D8DCDF";
    public string NodeFill { get; init; } = "#AEB5BA";
    public string Selection { get; init; } = "#21428E";
    public string Hover { get; init; } = "#1E6BFF";
}

public sealed record RouteMapDisplaySettings
{
    public double MapPadding { get; init; } = 18;
    public double CardColumnGap { get; init; } = 18;
    public double FragmentLength { get; init; } = 100;
    public double FragmentGap { get; init; } = 6;
    public RouteMapPaletteSettings Palette { get; init; } = new();
}

public sealed record RouteNodeStyle
{
    public double Radius { get; init; } = 15;
    public double InnerRadiusRatio { get; init; } = 0.38;
    public double BorderThickness { get; init; } = 2;
    public string FillColor { get; init; } = "#AEB5BA";
    public string BorderColor { get; init; } = "#DDE1E4";
    public string InnerColor { get; init; } = "#EEF1F3";
    public string LabelColor { get; init; } = "#77828D";
    public double LabelFontSize { get; init; } = 11;
    public string ActiveOutlineColor { get; init; } = "#00A6A6";
    public double ActiveOutlineThickness { get; init; } = 3;
}

public sealed record RouteTopBarButtonSettings
{
    public string Text { get; init; } = string.Empty;
    public string NormalBackground { get; init; } = "#ECEFF1";
    public string PressedBackground { get; init; } = "#949595";
    public string CheckedBackground { get; init; } = "#3378D6";
    public string NormalForeground { get; init; } = "#59636E";
    public string PressedForeground { get; init; } = "#FFFFFF";
    public string CheckedForeground { get; init; } = "#FFFFFF";
    public RouteCommandButtonKind ButtonKind { get; init; } = RouteCommandButtonKind.Toggle;
    public bool OffFeedbackEnabled { get; init; }
    public SignalBinding? OffFeedbackBinding { get; init; }
    public SignalBinding Binding { get; init; } = new(
        SignalBindingRole.AutomaticModeCommand,
        "system.mode.automatic",
        SignalBindingDirection.ReadWrite,
        Configurator.Application.Services.Signals.SignalValueType.Bool);
}

public sealed record RouteTopBarSettings(
    RouteTopBarButtonSettings Automatic,
    RouteTopBarButtonSettings Manual,
    RouteTopBarButtonSettings Emergency);

public sealed record RouteSegmentStyle
{
    public string NormalColor { get; init; } = "#C9CED0";
    public string ActiveColor { get; init; } = "#2D56B3";
    public double Thickness { get; init; } = 4;
    public double ActiveThickness { get; init; } = 5;
    public double? FragmentLength { get; init; }
    public double? FragmentGap { get; init; }
    public double EndpointGap { get; init; } = 6;
    public RouteLineCap LineCap { get; init; } = RouteLineCap.Round;
    public string LabelColor { get; init; } = "#77828D";
    public double LabelFontSize { get; init; } = 11;
}

public sealed record EquipmentCardStyle
{
    public double Width { get; init; } = 295;
    public double MinimumWidth { get; init; } = 250;
    public double Height { get; init; } = 141;
    public RouteThickness Margin { get; init; } = RouteThickness.Uniform(5);
    public RouteThickness Padding { get; init; } = new(12, 8, 6, 8);
    public string BackgroundColor { get; init; } = "#00FFFFFF";
    public string BorderColor { get; init; } = "#C8D0D7";
    public RouteThickness BorderThickness { get; init; } = new(2, 0, 0, 0);
    public RouteCornerRadius CornerRadius { get; init; } = RouteCornerRadius.Uniform(0);
    public string TitleColor { get; init; } = "#48525C";
    public string TextColor { get; init; } = "#48525C";
    public double TitleFontSize { get; init; } = 18;
    public double StatusFontSize { get; init; } = 16;
    public double RouteTextFontSize { get; init; } = 14;
    public double ActionFontSize { get; init; } = 18;
    public string StartText { get; init; } = "ПУСК";
    public string StopText { get; init; } = "СТОП";
    public string SendPrefix { get; init; } = "Отправить";
    public string ReturnPrefix { get; init; } = "Возврат";
    public string StartColor { get; init; } = "#D0D0D0";
    public string StartPressedColor { get; init; } = "#949595";
    public string StartCheckedColor { get; init; } = "#3A9D5D";
    public string StartForegroundColor { get; init; } = "#101820";
    public string StartPressedForegroundColor { get; init; } = "#101820";
    public string StartCheckedForegroundColor { get; init; } = "#FFFFFF";
    public string StopColor { get; init; } = "#D95D4E";
    public string StopPressedColor { get; init; } = "#949595";
    public string StopCheckedColor { get; init; } = "#9E2F25";
    public string StopForegroundColor { get; init; } = "#FFFFFF";
    public string StopPressedForegroundColor { get; init; } = "#FFFFFF";
    public string StopCheckedForegroundColor { get; init; } = "#FFFFFF";
}

public sealed record RoutePlaceholderStyle
{
    public string BackgroundColor { get; init; } = "#00FFFFFF";
    public string BorderColor { get; init; } = "#C8D0D7";
    public RouteThickness BorderThickness { get; init; } = new(2, 0, 0, 0);
    public RouteCornerRadius CornerRadius { get; init; } = RouteCornerRadius.Uniform(0);
    public RouteThickness Margin { get; init; } = RouteThickness.Uniform(5);
}

public sealed record RouteCardVerticalAnchor(
    RouteCardVerticalAnchorKind Kind,
    string? NodeId = null)
{
    public static RouteCardVerticalAnchor ChainBoundsCenter { get; } =
        new(RouteCardVerticalAnchorKind.ChainBoundsCenter);

    public static RouteCardVerticalAnchor Node(string nodeId)
    {
        return new RouteCardVerticalAnchor(RouteCardVerticalAnchorKind.Node, nodeId);
    }
}

public sealed record RouteMapDefinition(
    double LogicalWidth,
    double LogicalHeight,
    IReadOnlyList<RouteChain> Chains,
    IReadOnlyList<RouteNode> Nodes,
    IReadOnlyList<RouteSegment> Segments,
    IReadOnlyList<RouteVehicle> Vehicles,
    IReadOnlyList<EquipmentCommandCard> MapEquipment,
    IReadOnlyList<RequestItem> Requests,
    IReadOnlyList<RequestTemplateItem> RequestTemplates,
    RouteMapDisplaySettings? Display = null,
    IReadOnlyList<RoutePlaceholderRule>? PlaceholderRules = null,
    RouteTopBarSettings? TopBar = null);

public sealed record RouteChain(
    string Id,
    double X,
    double Y,
    IReadOnlyList<string> NodeIds,
    IReadOnlyList<string> SegmentIds,
    string? AttachedEquipmentCardId,
    double AttachedCardRightOffset,
    RouteCardVerticalAnchor AttachedCardVerticalAnchor);

public sealed record RouteNode(
    string Id,
    string Title,
    double X,
    double Y,
    RouteNodeKind Kind,
    RouteObjectState State,
    IReadOnlyList<SignalBinding> Bindings,
    double LabelOffsetX = -24,
    double LabelOffsetY = 24,
    RouteNodeLabelPlacement LabelPlacement = RouteNodeLabelPlacement.Below,
    bool IsLoader = false,
    bool IsTarget = false,
    RouteNodeMenuKind MenuKind = RouteNodeMenuKind.None,
    bool IsVisible = true,
    RouteNodeStyle? Style = null);

public sealed record RouteNodeRoleState(
    string ObjectId,
    bool IsLoader,
    bool IsTarget);

public static class RouteNodeRoleStateTransitions
{
    public static IReadOnlyDictionary<string, RouteNodeRoleState> ToggleLoader(
        IReadOnlyDictionary<string, RouteNodeRoleState> states,
        string objectId)
    {
        if (!states.TryGetValue(objectId, out var selected))
            return states;

        var shouldEnable = !selected.IsLoader;

        return states.ToDictionary(
            x => x.Key,
            x =>
            {
                if (x.Key == objectId)
                    return x.Value with { IsLoader = shouldEnable, IsTarget = false };

                return x.Value with { IsLoader = false };
            });
    }

    public static IReadOnlyDictionary<string, RouteNodeRoleState> ToggleTarget(
        IReadOnlyDictionary<string, RouteNodeRoleState> states,
        string objectId)
    {
        if (!states.TryGetValue(objectId, out var selected))
            return states;

        var shouldEnable = !selected.IsTarget;

        return states.ToDictionary(
            x => x.Key,
            x =>
            {
                if (x.Key == objectId)
                    return x.Value with { IsLoader = false, IsTarget = shouldEnable };

                return x.Value with { IsTarget = false };
            });
    }

    public static RouteNodeRoleState ToggleLoader(RouteNodeRoleState state)
    {
        return state.IsLoader
            ? state with { IsLoader = false }
            : state with { IsLoader = true, IsTarget = false };
    }

    public static RouteNodeRoleState ToggleTarget(RouteNodeRoleState state)
    {
        return state.IsTarget
            ? state with { IsTarget = false }
            : state with { IsLoader = false, IsTarget = true };
    }

}

public sealed record RouteSegment(
    string Id,
    string FromNodeId,
    string ToNodeId,
    RouteObjectState State,
    bool IsDirectional,
    IReadOnlyList<SignalBinding> Bindings,
    RouteSegmentKind Kind = RouteSegmentKind.Straight,
    double ArcRadius = 0,
    RouteElbowOrder ElbowOrder = RouteElbowOrder.VerticalThenHorizontal,
    string? Title = null,
    double LabelOffsetX = 0,
    double LabelOffsetY = 0,
    bool IsVisible = true,
    RouteSegmentStyle? Style = null,
    IReadOnlyList<RouteSegmentActiveFragment>? ActiveFragments = null);

public sealed record RouteSegmentActiveFragment(
    int Index,
    SignalBinding Binding);

public sealed record RouteVehicle(
    string Id,
    string Title,
    double X,
    double Y,
    RouteObjectState State,
    IReadOnlyList<SignalBinding> Bindings);

public sealed record EquipmentCommandCard(
    string Id,
    string Title,
    string StatusText,
    RouteObjectState State,
    bool CanStart,
    bool CanStop,
    IReadOnlyList<SignalBinding> Bindings,
    RouteCommandButtonKind StartButtonKind = RouteCommandButtonKind.Toggle,
    RouteCommandButtonKind StopButtonKind = RouteCommandButtonKind.Toggle,
    bool StartOffFeedbackEnabled = false,
    bool StopOffFeedbackEnabled = false,
    bool IsVisible = true,
    string? AttachedChainId = null,
    double AttachedCardRightOffset = 0,
    RouteCardVerticalAnchor? VerticalAnchor = null,
    EquipmentCardStyle? Style = null);

public sealed record RoutePlaceholderRule(
    string Id,
    string CardId,
    RoutePlaceholderPlacement Placement,
    RoutePlaceholderHeightMode HeightMode,
    double FixedHeight,
    double Gap,
    int? MaximumCount,
    bool IsVisible,
    RoutePlaceholderStyle? Style = null);

public sealed record RequestItem(
    int Number,
    string Recipe,
    string Volume,
    string Loading,
    string Unloading);

public sealed record RequestTemplateItem(
    string Id,
    string Title,
    string Recipe,
    string Volume);
