using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.Controls;

public sealed class RouteMapControl : Control
{
    public static readonly StyledProperty<RouteMapDefinition?> DefinitionProperty =
        AvaloniaProperty.Register<RouteMapControl, RouteMapDefinition?>(nameof(Definition));

    public static readonly StyledProperty<RouteMapRuntimeState?> RuntimeStateProperty =
        AvaloniaProperty.Register<RouteMapControl, RouteMapRuntimeState?>(nameof(RuntimeState));

    public static readonly StyledProperty<string?> SelectedObjectIdProperty =
        AvaloniaProperty.Register<RouteMapControl, string?>(
            nameof(SelectedObjectId),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> HoveredObjectIdProperty =
        AvaloniaProperty.Register<RouteMapControl, string?>(nameof(HoveredObjectId));

    public static readonly StyledProperty<IReadOnlyDictionary<string, RouteNodeRoleState>?> NodeRoleStatesProperty =
        AvaloniaProperty.Register<RouteMapControl, IReadOnlyDictionary<string, RouteNodeRoleState>?>(nameof(NodeRoleStates));

    public static readonly StyledProperty<ICommand?> ToggleNodeTargetCommandProperty =
        AvaloniaProperty.Register<RouteMapControl, ICommand?>(nameof(ToggleNodeTargetCommand));

    public static readonly StyledProperty<ICommand?> ToggleNodeLoaderCommandProperty =
        AvaloniaProperty.Register<RouteMapControl, ICommand?>(nameof(ToggleNodeLoaderCommand));

    public static readonly StyledProperty<bool> AreCommandsEnabledProperty =
        AvaloniaProperty.Register<RouteMapControl, bool>(nameof(AreCommandsEnabled), defaultValue: true);

    private MenuFlyout? _activeNodeFlyout;

    static RouteMapControl()
    {
        AffectsRender<RouteMapControl>(
            DefinitionProperty,
            RuntimeStateProperty,
            SelectedObjectIdProperty,
            HoveredObjectIdProperty,
            NodeRoleStatesProperty);
    }

    public RouteMapDefinition? Definition
    {
        get => GetValue(DefinitionProperty);
        set => SetValue(DefinitionProperty, value);
    }

    public RouteMapRuntimeState? RuntimeState
    {
        get => GetValue(RuntimeStateProperty);
        set => SetValue(RuntimeStateProperty, value);
    }

    public string? SelectedObjectId
    {
        get => GetValue(SelectedObjectIdProperty);
        set => SetValue(SelectedObjectIdProperty, value);
    }

    public string? HoveredObjectId
    {
        get => GetValue(HoveredObjectIdProperty);
        set => SetValue(HoveredObjectIdProperty, value);
    }

    public IReadOnlyDictionary<string, RouteNodeRoleState>? NodeRoleStates
    {
        get => GetValue(NodeRoleStatesProperty);
        set => SetValue(NodeRoleStatesProperty, value);
    }

    public ICommand? ToggleNodeTargetCommand
    {
        get => GetValue(ToggleNodeTargetCommandProperty);
        set => SetValue(ToggleNodeTargetCommandProperty, value);
    }

    public ICommand? ToggleNodeLoaderCommand
    {
        get => GetValue(ToggleNodeLoaderCommandProperty);
        set => SetValue(ToggleNodeLoaderCommandProperty, value);
    }

    public bool AreCommandsEnabled
    {
        get => GetValue(AreCommandsEnabledProperty);
        set => SetValue(AreCommandsEnabledProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var display = Definition?.Display ?? new RouteMapDisplaySettings();
        context.DrawRectangle(RouteMapPalette.Brush(display.Palette.Background), null, new Rect(Bounds.Size));

        if (Definition is null)
            return;

        var runtimeState = RuntimeState ?? RouteMapRuntimeState.Empty;
        var transform = RouteMapViewportLayout.Create(
            Definition,
            Bounds.Width,
            Bounds.Height).Transform;

        DrawSegments(context, Definition, runtimeState, transform);
        DrawNodes(context, Definition, runtimeState, transform);
        DrawVehicles(context, Definition, runtimeState, transform);
        DrawSegmentLabels(context, Definition, runtimeState, transform);
        DrawLabels(context, Definition, runtimeState, transform);
        DrawSelection(context, Definition, runtimeState, transform);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var hit = HitTest(e.GetPosition(this));
        HoveredObjectId = hit?.ObjectId;
        Cursor = hit is null
            ? Cursor.Default
            : new Cursor(StandardCursorType.Hand);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        HoveredObjectId = null;
        Cursor = Cursor.Default;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var hit = HitTest(e.GetPosition(this));
        SelectedObjectId = hit?.ObjectId;
        HideActiveNodeFlyout();

        if (hit?.Kind == RouteMapHitKind.Node)
            ShowNodeFlyout(hit.ObjectId);
    }

    private RouteMapHitResult? HitTest(Point viewPoint)
    {
        if (Definition is null)
            return null;

        var transform = RouteMapViewportLayout.Create(
            Definition,
            Bounds.Width,
            Bounds.Height).Transform;

        return RouteMapHitTester.HitTest(
            Definition,
            RuntimeState ?? RouteMapRuntimeState.Empty,
            viewPoint,
            transform);
    }

    private static void DrawSegments(
        DrawingContext context,
        RouteMapDefinition definition,
        RouteMapRuntimeState runtimeState,
        RouteMapTransform transform)
    {
        var nodesById = definition.Nodes.ToDictionary(x => x.Id);

        foreach (var segment in definition.Segments)
        {
            if (!segment.IsVisible || !IsRuntimeVisible(runtimeState, segment.Id))
                continue;

            if (!nodesById.TryGetValue(segment.FromNodeId, out var from) ||
                !nodesById.TryGetValue(segment.ToNodeId, out var to))
                continue;

            var p1 = transform.ToViewPoint(from.X, from.Y);
            var p2 = transform.ToViewPoint(to.X, to.Y);
            var viewPath = RouteSegmentGeometry.Create(
                segment with { ArcRadius = segment.ArcRadius * transform.Scale },
                p1,
                p2);
            var display = definition.Display ?? new RouteMapDisplaySettings();
            var style = segment.Style ?? new RouteSegmentStyle();
            var state = GetState(runtimeState, segment.Id, segment.State);
            var runtime = runtimeState.Find(segment.Id);
            var logicalPath = RouteSegmentGeometry.Create(
                segment,
                new Point(from.X, from.Y),
                new Point(to.X, to.Y));
            var logicalRanges = RouteSegmentGeometry.CalculateLogicalDrawableRanges(segment, from, to, display);
            var viewRanges = segment.ActiveFragments is { Count: > 0 }
                ? RouteSegmentGeometry.ProjectRanges(logicalRanges, logicalPath.Length, viewPath.Length)
                : RouteSegmentGeometry.CalculateViewDrawableRanges(viewPath, segment, from, to, display);
            if (viewRanges.Count == 0)
                continue;

            var baseState = state is RouteObjectState.ActiveRoute or RouteObjectState.Running
                ? RouteObjectState.Idle
                : state;
            var basePen = RouteMapPalette.TrackPenForState(baseState, display.Palette, style);
            foreach (var range in viewRanges)
                context.DrawGeometry(null, basePen, RouteSegmentGeometry.CreateGeometry(viewPath, range));

            if (state is RouteObjectState.Fault or RouteObjectState.Offline or RouteObjectState.Disabled)
                continue;

            var activeRanges = ActiveSegmentRanges(state, runtime, viewRanges);
            if (activeRanges.Count == 0)
                continue;

            var activePen = RouteMapPalette.TrackPenForState(RouteObjectState.ActiveRoute, display.Palette, style);
            foreach (var range in activeRanges)
                context.DrawGeometry(null, activePen, RouteSegmentGeometry.CreateGeometry(viewPath, range));
        }
    }

    internal static IReadOnlyList<RoutePathRange> ActiveSegmentRanges(
        RouteObjectState state,
        RouteObjectRuntimeState? runtime,
        IReadOnlyList<RoutePathRange> visualRanges)
    {
        if (visualRanges.Count == 0)
            return Array.Empty<RoutePathRange>();

        if (state is RouteObjectState.Fault or RouteObjectState.Offline or RouteObjectState.Disabled)
            return Array.Empty<RoutePathRange>();

        if (state is RouteObjectState.ActiveRoute or RouteObjectState.Running)
            return visualRanges;

        if (runtime?.ActiveFragmentIndexes is null || runtime.ActiveFragmentIndexes.Count == 0)
            return Array.Empty<RoutePathRange>();

        return visualRanges
            .Select((range, index) => (Range: range, Index: index + 1))
            .Where(item => runtime.ActiveFragmentIndexes.Contains(item.Index))
            .Select(item => item.Range)
            .ToArray();
    }

    private static void DrawSegmentLabels(
        DrawingContext context,
        RouteMapDefinition definition,
        RouteMapRuntimeState runtimeState,
        RouteMapTransform transform)
    {
        var nodesById = definition.Nodes.ToDictionary(x => x.Id);

        foreach (var segment in definition.Segments)
        {
            if (string.IsNullOrWhiteSpace(segment.Title) ||
                !segment.IsVisible || !IsRuntimeVisible(runtimeState, segment.Id) ||
                !nodesById.TryGetValue(segment.FromNodeId, out var from) ||
                !nodesById.TryGetValue(segment.ToNodeId, out var to))
                continue;

            var path = RouteSegmentGeometry.Create(
                segment with { ArcRadius = segment.ArcRadius * transform.Scale },
                transform.ToViewPoint(from.X, from.Y),
                transform.ToViewPoint(to.X, to.Y));
            var anchor = path.Parts.OfType<RouteArcPathPart>().FirstOrDefault() is { } arc
                ? arc.PointAt(arc.Length / 2)
                : path.PointAt(path.Length / 2);

            var style = segment.Style ?? new RouteSegmentStyle();
            DrawText(
                context,
                segment.Title,
                new Point(anchor.X + segment.LabelOffsetX, anchor.Y + segment.LabelOffsetY),
                RouteMapPalette.Brush(style.LabelColor),
                style.LabelFontSize);
        }
    }

    private void DrawNodes(
        DrawingContext context,
        RouteMapDefinition definition,
        RouteMapRuntimeState runtimeState,
        RouteMapTransform transform)
    {
        foreach (var node in definition.Nodes)
        {
            if (!node.IsVisible || !IsRuntimeVisible(runtimeState, node.Id))
                continue;

            var center = transform.ToViewPoint(node.X, node.Y);
            var state = GetState(runtimeState, node.Id, node.State);
            var palette = (definition.Display ?? new RouteMapDisplaySettings()).Palette;
            var style = node.Style ?? new RouteNodeStyle();
            var radius = RouteMapNodeMetrics.RadiusForNode(node);
            var brush = BrushForNode(node, state, GetRoleState(node.Id), palette);

            context.DrawEllipse(brush, new Pen(RouteMapPalette.Brush(style.BorderColor), style.BorderThickness), center, radius, radius);
            context.DrawEllipse(RouteMapPalette.Brush(style.InnerColor), null, center, radius * style.InnerRadiusRatio, radius * style.InnerRadiusRatio);

            var objectRuntime = runtimeState.Find(node.Id);
            if (ShouldDrawActiveOutline(objectRuntime))
            {
                var outlineRadius = radius + style.ActiveOutlineThickness / 2 + 1;
                context.DrawEllipse(
                    null,
                    new Pen(RouteMapPalette.Brush(style.ActiveOutlineColor), style.ActiveOutlineThickness),
                    center,
                    outlineRadius,
                    outlineRadius);
            }
        }
    }

    private static void DrawVehicles(
        DrawingContext context,
        RouteMapDefinition definition,
        RouteMapRuntimeState runtimeState,
        RouteMapTransform transform)
    {
        foreach (var vehicle in definition.Vehicles)
        {
            if (!IsRuntimeVisible(runtimeState, vehicle.Id))
                continue;

            var state = GetState(runtimeState, vehicle.Id, vehicle.State);
            var center = transform.ToViewPoint(vehicle.X, vehicle.Y);
            var size = 28;
            var rect = new Rect(center.X - size / 2, center.Y - size / 2, size, size);
            var pen = state == RouteObjectState.ActiveRoute
                ? RouteMapPalette.SelectionPen
                : new Pen(RouteMapPalette.BrushForState(state), 2);

            context.DrawRectangle(new SolidColorBrush(Color.Parse("#EEF2F6")), pen, rect, 4, 4);
        }
    }

    private static void DrawLabels(
        DrawingContext context,
        RouteMapDefinition definition,
        RouteMapRuntimeState runtimeState,
        RouteMapTransform transform)
    {
        foreach (var node in definition.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Title) || !node.IsVisible || !IsRuntimeVisible(runtimeState, node.Id))
                continue;

            var style = node.Style ?? new RouteNodeStyle();
            DrawNodeLabel(context, node, transform, RouteMapPalette.Brush(style.LabelColor), style.LabelFontSize);
        }

        foreach (var vehicle in definition.Vehicles)
        {
            if (!IsRuntimeVisible(runtimeState, vehicle.Id))
                continue;

            var point = transform.ToViewPoint(vehicle.X, vehicle.Y);
            DrawText(
                context,
                vehicle.Title,
                new Point(point.X - 18, point.Y + 20),
                RouteMapPalette.MutedTextBrush,
                10);
        }
    }

    private void DrawSelection(
        DrawingContext context,
        RouteMapDefinition definition,
        RouteMapRuntimeState runtimeState,
        RouteMapTransform transform)
    {
        var palette = (definition.Display ?? new RouteMapDisplaySettings()).Palette;
        DrawObjectMarker(context, definition, runtimeState, transform, HoveredObjectId, new Pen(RouteMapPalette.Brush(palette.Hover), 2), radiusPadding: 3);
        DrawObjectMarker(context, definition, runtimeState, transform, SelectedObjectId, new Pen(RouteMapPalette.Brush(palette.Selection), 3), radiusPadding: 6);
    }

    private static void DrawObjectMarker(
        DrawingContext context,
        RouteMapDefinition definition,
        RouteMapRuntimeState runtimeState,
        RouteMapTransform transform,
        string? objectId,
        Pen pen,
        double radiusPadding)
    {
        if (string.IsNullOrWhiteSpace(objectId))
            return;

        var node = definition.Nodes.FirstOrDefault(x => x.Id == objectId);
        if (node is not null && node.IsVisible && IsRuntimeVisible(runtimeState, node.Id))
        {
            var center = transform.ToViewPoint(node.X, node.Y);
            var style = node.Style ?? new RouteNodeStyle();
            var activePadding = ShouldDrawActiveOutline(runtimeState.Find(node.Id))
                ? style.ActiveOutlineThickness + 2
                : 0;
            var radius = RouteMapNodeMetrics.RadiusForNode(node) + activePadding + radiusPadding;
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var vehicle = definition.Vehicles.FirstOrDefault(x => x.Id == objectId);
        if (vehicle is not null && IsRuntimeVisible(runtimeState, vehicle.Id))
        {
            var center = transform.ToViewPoint(vehicle.X, vehicle.Y);
            var size = 42 + radiusPadding;
            var rect = new Rect(center.X - size / 2, center.Y - size / 2, size, size);
            context.DrawRectangle(null, pen, rect, 5, 5);
            return;
        }

        return;
    }

    internal static bool ShouldDrawActiveOutline(RouteObjectRuntimeState? runtimeState) =>
        runtimeState?.IsSignalActive == true &&
        runtimeState.State is not RouteObjectState.Offline
            and not RouteObjectState.Fault
            and not RouteObjectState.Disabled;

    private static void DrawText(DrawingContext context, string text, Point origin, IBrush brush, double fontSize)
    {
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            fontSize,
            brush);

        context.DrawText(formattedText, origin);
    }

    private static void DrawNodeLabel(
        DrawingContext context,
        RouteNode node,
        RouteMapTransform transform,
        IBrush brush,
        double fontSize)
    {
        var lines = node.Title.Split('\n', StringSplitOptions.None)
            .Select(line => CreateFormattedText(line, brush, fontSize))
            .ToArray();
        var layout = RouteNodeLabelLayout.Calculate(node, transform, lines.Sum(x => x.Height));
        var y = layout.Top;

        foreach (var formattedText in lines)
        {
            var x = layout.IsHorizontallyCentered
                ? layout.X - formattedText.Width / 2
                : layout.X;
            context.DrawText(formattedText, new Point(x, y));
            y += formattedText.Height;
        }
    }

    private static FormattedText CreateFormattedText(string text, IBrush brush, double fontSize)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            fontSize,
            brush);
    }

    private static RouteObjectState GetState(
        RouteMapRuntimeState runtimeState,
        string objectId,
        RouteObjectState fallback)
    {
        return runtimeState.Find(objectId)?.State ?? fallback;
    }

    private RouteNodeRoleState? GetRoleState(string objectId)
    {
        return NodeRoleStates is not null && NodeRoleStates.TryGetValue(objectId, out var state)
            ? state
            : null;
    }

    private static IBrush BrushForNode(
        RouteNode node,
        RouteObjectState runtimeState,
        RouteNodeRoleState? roleState,
        RouteMapPaletteSettings palette)
    {
        if (roleState?.IsTarget == true)
            return RouteMapPalette.Brush(palette.Ready);

        if (roleState?.IsLoader == true)
            return RouteMapPalette.Brush(palette.Warning);

        return RouteMapPalette.BrushForState(runtimeState, palette, (node.Style ?? new RouteNodeStyle()).FillColor);
    }

    private static bool IsRuntimeVisible(RouteMapRuntimeState runtimeState, string objectId)
    {
        return runtimeState.Find(objectId)?.IsVisible ?? true;
    }

    private void ShowNodeFlyout(string objectId)
    {
        if (Definition is null)
            return;

        var node = Definition.Nodes.FirstOrDefault(x => x.Id == objectId);
        if (node is null || node.MenuKind == RouteNodeMenuKind.None)
            return;

        var roleState = GetRoleState(node.Id) ?? new RouteNodeRoleState(node.Id, node.IsLoader, node.IsTarget);
        var items = new List<MenuItem>();

        if (node.MenuKind is RouteNodeMenuKind.SendOnly or RouteNodeMenuKind.SendAndReturn)
            items.Add(CreateNodeMenuItem("Отправить", node.Id, roleState.IsTarget, ToggleNodeTargetCommand));

        if (node.MenuKind == RouteNodeMenuKind.SendAndReturn)
            items.Add(CreateNodeMenuItem("Возврат", node.Id, roleState.IsLoader, ToggleNodeLoaderCommand));

        if (items.Count == 0)
            return;

        var flyout = new MenuFlyout
        {
            ItemsSource = items,
        };

        _activeNodeFlyout = flyout;
        flyout.ShowAt(this, showAtPointer: true);
    }

    internal MenuItem CreateNodeMenuItem(string header, string objectId, bool isChecked, ICommand? command)
    {
        var item = new MenuItem
        {
            Header = header,
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = isChecked,
            IsEnabled = AreCommandsEnabled,
            Command = command,
            CommandParameter = objectId,
        };

        item.Click += (_, _) => HideActiveNodeFlyout();
        return item;
    }

    private void HideActiveNodeFlyout()
    {
        _activeNodeFlyout?.Hide();
        _activeNodeFlyout = null;
    }
}

internal static class RouteMapNodeMetrics
{
    public const double Radius = 15;

    public static double RadiusForKind(RouteNodeKind kind)
    {
        return Radius;
    }

    public static double RadiusForNode(RouteNode node) => (node.Style ?? new RouteNodeStyle()).Radius;
}

internal readonly record struct RouteNodeLabelPosition(
    double X,
    double Top,
    bool IsHorizontallyCentered);

internal static class RouteNodeLabelLayout
{
    public static RouteNodeLabelPosition Calculate(
        RouteNode node,
        RouteMapTransform transform,
        double textHeight = 0)
    {
        var center = transform.ToViewPoint(node.X, node.Y);
        var radius = RouteMapNodeMetrics.RadiusForNode(node);
        return node.LabelPlacement == RouteNodeLabelPlacement.Right
            ? new RouteNodeLabelPosition(
                center.X + radius + node.LabelOffsetX,
                center.Y - textHeight / 2 + node.LabelOffsetY,
                IsHorizontallyCentered: false)
            : new RouteNodeLabelPosition(
                center.X + node.LabelOffsetX,
                center.Y + radius + node.LabelOffsetY,
                IsHorizontallyCentered: true);
    }
}
