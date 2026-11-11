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

    public static readonly StyledProperty<double> MeasuredCardHeightProperty =
        AvaloniaProperty.Register<RouteMapControl, double>(nameof(MeasuredCardHeight));

    internal MenuFlyout? ActiveNodeFlyout { get; private set; }

    static RouteMapControl()
    {
        AffectsRender<RouteMapControl>(
            DefinitionProperty,
            RuntimeStateProperty,
            SelectedObjectIdProperty,
            HoveredObjectIdProperty,
            NodeRoleStatesProperty,
            MeasuredCardHeightProperty);
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

    public double MeasuredCardHeight
    {
        get => GetValue(MeasuredCardHeightProperty);
        set => SetValue(MeasuredCardHeightProperty, value);
    }

    internal RouteMapViewportLayout CreateViewportLayout(RouteMapDefinition definition) =>
        RouteMapViewportLayout.Create(definition, Bounds.Width, Bounds.Height, MeasuredCardHeight);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var display = Definition?.Display ?? new RouteMapDisplaySettings();
        context.DrawRectangle(RouteMapPalette.Brush(display.Palette.Background), null, new Rect(Bounds.Size));

        if (Definition is null)
            return;

        var runtimeState = RuntimeState ?? RouteMapRuntimeState.Empty;
        var layout = CreateViewportLayout(Definition);
        var transform = layout.Transform;

        DrawSegments(context, Definition, runtimeState, layout);
        DrawNodes(context, Definition, runtimeState, transform);
        DrawVehicles(context, Definition, runtimeState, transform);
        DrawSegmentLabels(context, Definition, runtimeState, layout);
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

        var transform = CreateViewportLayout(Definition).Transform;

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
        RouteMapViewportLayout layout)
    {
        var transform = layout.Transform;
        var nodesById = definition.Nodes.ToDictionary(x => x.Id);
        var display = definition.Display ?? new RouteMapDisplaySettings();

        foreach (var segment in definition.Segments)
        {
            if (!segment.IsVisible || !IsRuntimeVisible(runtimeState, segment.Id))
                continue;

            if (!nodesById.TryGetValue(segment.FromNodeId, out var from) ||
                !nodesById.TryGetValue(segment.ToNodeId, out var to))
                continue;

            var isTerminal = segment.Kind == RouteSegmentKind.DeadEnd;
            var style = segment.Style ?? new RouteSegmentStyle();
            var cap = default(RouteTerminalCap);
            var p1 = transform.ToViewPoint(from.X, from.Y);
            var p2 = transform.ToViewPoint(to.X, to.Y);
            if (isTerminal && !RouteSegmentGeometry.TryCreateTerminalViewGeometry(
                    segment, from, to, style, transform, layout.MapViewBounds, out p1, out p2, out cap))
                continue;

            var viewPath = RouteSegmentGeometry.Create(segment, p1, p2, transform.Scale);
            var state = GetState(runtimeState, segment.Id, segment.State);
            var runtime = runtimeState.Find(segment.Id);
            var hasFragments = segment.ActiveFragments is { Count: > 0 };
            var logicalRanges = hasFragments
                ? RouteSegmentGeometry.CalculateLogicalDrawableRanges(segment, from, to, display)
                : Array.Empty<RoutePathRange>();
            IReadOnlyList<RoutePathRange> viewRanges;
            if (isTerminal)
            {
                var station = segment.TerminalSide == RouteTerminalSide.From ? to : from;
                viewRanges = RouteSegmentGeometry.CalculateTerminalViewDrawableRanges(
                    segment, station, style, display, viewPath.Length, transform.Scale, logicalRanges, transform.NodeScale);
                style = style with
                {
                    Thickness = style.Thickness * transform.Scale,
                    ActiveThickness = RouteSegmentGeometry.TerminalActiveThickness(style) * transform.Scale,
                };
            }
            else if (hasFragments)
            {
                var logicalPath = RouteSegmentGeometry.Create(segment, new Point(from.X, from.Y), new Point(to.X, to.Y));
                viewRanges = RouteSegmentGeometry.ProjectRanges(logicalRanges, logicalPath.Length, viewPath.Length);
                if (layout.FitFragmentRanges)
                {
                    viewRanges = RouteSegmentGeometry.FitViewFragmentRanges(
                        viewRanges, viewPath.Length, from, to, style, transform.NodeScale, transform.Scale);
                }
            }
            else
            {
                viewRanges = RouteSegmentGeometry.CalculateViewDrawableRanges(viewPath, segment, from, to, display, transform.NodeScale);
            }

            var baseState = state is RouteObjectState.ActiveRoute or RouteObjectState.Running
                ? RouteObjectState.Idle
                : state;
            var basePen = viewRanges.Count > 0
                ? RouteMapPalette.TrackPenForState(baseState, display.Palette, style)
                : null;
            if (basePen is not null)
            {
                foreach (var range in viewRanges)
                    context.DrawGeometry(null, basePen, RouteSegmentGeometry.CreateGeometry(viewPath, range));
            }

            var activeRanges = ActiveSegmentRanges(state, runtime, viewRanges);
            if (activeRanges.Count > 0)
            {
                var activePen = RouteMapPalette.TrackPenForState(RouteObjectState.ActiveRoute, display.Palette, style);
                // A wider round active stroke must not protrude behind the neutral bar.
                using var clip = isTerminal && activePen.LineCap == PenLineCap.Round && activePen.Thickness > style.Thickness
                    ? context.PushGeometryClip(RouteSegmentGeometry.CreateTerminalBodyClip(segment, p1, p2, activePen.Thickness))
                    : default;
                foreach (var range in activeRanges)
                    context.DrawGeometry(null, activePen, RouteSegmentGeometry.CreateGeometry(viewPath, range));
            }

            if (isTerminal)
            {
                var capPen = baseState == RouteObjectState.Idle && basePen is not null
                    ? basePen
                    : RouteMapPalette.TrackPenForState(RouteObjectState.Idle, display.Palette, style);
                context.DrawLine(capPen, cap.Start, cap.End);
            }
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
        RouteMapViewportLayout layout)
    {
        var transform = layout.Transform;
        var nodesById = definition.Nodes.ToDictionary(x => x.Id);

        foreach (var segment in definition.Segments)
        {
            if (string.IsNullOrWhiteSpace(segment.Title) ||
                !segment.IsVisible || !IsRuntimeVisible(runtimeState, segment.Id) ||
                !nodesById.TryGetValue(segment.FromNodeId, out var from) ||
                !nodesById.TryGetValue(segment.ToNodeId, out var to))
                continue;

            var style = segment.Style ?? new RouteSegmentStyle();
            var state = GetState(runtimeState, segment.Id, segment.State);
            var palette = (definition.Display ?? new RouteMapDisplaySettings()).Palette;
            var labelBrush = state switch
            {
                RouteObjectState.Offline => RouteMapPalette.Brush(palette.Offline),
                RouteObjectState.Disabled => RouteMapPalette.Brush(palette.Disabled),
                _ => RouteMapPalette.Brush(style.LabelColor),
            };
            var text = CreateFormattedText(segment.Title, labelBrush, style.LabelFontSize);
            var offset = new Vector(segment.LabelOffsetX, segment.LabelOffsetY);
            if (segment.Kind == RouteSegmentKind.DeadEnd && segment.LabelAtTerminal)
            {
                if (!RouteSegmentGeometry.TryCreateTerminalViewGeometry(
                        segment, from, to, style, transform, layout.MapViewBounds, out var start, out var end, out var cap))
                    continue;

                var outward = segment.TerminalSide == RouteTerminalSide.From ? start - end : end - start;
                var capBounds = RouteSegmentGeometry.BoundsOf(cap.Start, cap.End).Inflate(style.Thickness * transform.Scale / 2);
                context.DrawText(text, GetTerminalLabelOrigin(capBounds, outward, text.Width, text.Height) + offset);
                continue;
            }

            var path = RouteSegmentGeometry.Create(
                segment,
                transform.ToViewPoint(from.X, from.Y),
                transform.ToViewPoint(to.X, to.Y),
                transform.Scale);
            if (path.Parts.Count == 0 && segment.Kind == RouteSegmentKind.DeadEnd)
                continue;
            var anchor = path.Parts.OfType<RouteArcPathPart>().FirstOrDefault() is { } arc
                ? arc.PointAt(arc.Length / 2)
                : path.PointAt(path.Length / 2);
            context.DrawText(text, anchor + offset);
        }
    }

    internal static Point GetTerminalLabelOrigin(Rect capBounds, Vector outward, double textWidth, double textHeight)
    {
        const double gap = 6;
        if (Math.Abs(outward.X) >= Math.Abs(outward.Y))
            return new Point(outward.X >= 0 ? capBounds.Right + gap : capBounds.Left - gap - textWidth,
                capBounds.Center.Y - textHeight / 2);

        return new Point(capBounds.Center.X - textWidth / 2,
            outward.Y >= 0 ? capBounds.Bottom + gap : capBounds.Top - gap - textHeight);
    }

    private void DrawNodes(
        DrawingContext context,
        RouteMapDefinition definition,
        RouteMapRuntimeState runtimeState,
        RouteMapTransform transform)
    {
        foreach (var node in definition.Nodes)
        {
            if (node.Kind == RouteNodeKind.TerminalAnchor || !node.IsVisible || !IsRuntimeVisible(runtimeState, node.Id))
                continue;

            var center = transform.ToViewPoint(node.X, node.Y);
            var state = GetState(runtimeState, node.Id, node.State);
            var palette = (definition.Display ?? new RouteMapDisplaySettings()).Palette;
            var style = node.Style ?? new RouteNodeStyle();
            var radius = RouteMapNodeMetrics.RadiusForNode(node) * transform.NodeScale;
            var brush = BrushForNode(node, state, GetRoleState(node.Id), palette);

            context.DrawEllipse(brush, new Pen(RouteMapPalette.Brush(style.BorderColor), style.BorderThickness * transform.NodeScale), center, radius, radius);
            if (style.InnerRadiusRatio > 0)
            {
                var innerBrush = state is RouteObjectState.Offline or RouteObjectState.Disabled
                    ? brush
                    : RouteMapPalette.Brush(style.InnerColor);
                var innerRadius = radius * style.InnerRadiusRatio;
                context.DrawEllipse(innerBrush, null, center, innerRadius, innerRadius);
            }

            var objectRuntime = runtimeState.Find(node.Id);
            if (ShouldDrawActiveOutline(objectRuntime))
            {
                var outlineThickness = style.ActiveOutlineThickness * transform.NodeScale;
                var outlineRadius = radius + outlineThickness / 2 + 1;
                context.DrawEllipse(
                    null,
                    new Pen(RouteMapPalette.Brush(style.ActiveOutlineColor), outlineThickness),
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
            if (node.Kind == RouteNodeKind.TerminalAnchor || string.IsNullOrWhiteSpace(node.Title) ||
                !node.IsVisible || !IsRuntimeVisible(runtimeState, node.Id))
                continue;

            var style = node.Style ?? new RouteNodeStyle();
            var state = GetState(runtimeState, node.Id, node.State);
            var palette = (definition.Display ?? new RouteMapDisplaySettings()).Palette;
            var labelBrush = state switch
            {
                RouteObjectState.Offline => RouteMapPalette.Brush(palette.Offline),
                RouteObjectState.Disabled => RouteMapPalette.Brush(palette.Disabled),
                _ => RouteMapPalette.Brush(style.LabelColor),
            };
            DrawNodeLabel(context, node, transform, labelBrush, style.LabelFontSize);
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
        if (node is not null && node.Kind != RouteNodeKind.TerminalAnchor && node.IsVisible &&
            IsRuntimeVisible(runtimeState, node.Id) && IsRuntimeEnabled(runtimeState, node.Id))
        {
            var center = transform.ToViewPoint(node.X, node.Y);
            var style = node.Style ?? new RouteNodeStyle();
            var activePadding = ShouldDrawActiveOutline(runtimeState.Find(node.Id))
                ? style.ActiveOutlineThickness + 2
                : 0;
            var radius = (RouteMapNodeMetrics.RadiusForNode(node) + activePadding + radiusPadding) * transform.NodeScale;
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
        if (runtimeState == RouteObjectState.Disabled)
            return RouteMapPalette.Brush(palette.Disabled);

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

    private static bool IsRuntimeEnabled(RouteMapRuntimeState runtimeState, string objectId)
    {
        return runtimeState.Find(objectId)?.IsEnabled ?? true;
    }

    private void ShowNodeFlyout(string objectId)
    {
        if (Definition is null)
            return;

        var node = Definition.Nodes.FirstOrDefault(x => x.Id == objectId);
        if (node is null || node.Kind == RouteNodeKind.TerminalAnchor || node.MenuKind == RouteNodeMenuKind.None ||
            !AreCommandsEnabled || !IsRuntimeEnabled(RuntimeState ?? RouteMapRuntimeState.Empty, node.Id))
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

        ActiveNodeFlyout = flyout;
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
        ActiveNodeFlyout?.Hide();
        ActiveNodeFlyout = null;
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
        var radius = RouteMapNodeMetrics.RadiusForNode(node) * transform.NodeScale;
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
