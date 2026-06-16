using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;

namespace Configurator.Desktop.Workspace.RouteMap.Panels;

public sealed class RouteMapAttachedCardsLayer : Panel
{
    public static readonly StyledProperty<RouteMapDefinition?> DefinitionProperty =
        AvaloniaProperty.Register<RouteMapAttachedCardsLayer, RouteMapDefinition?>(nameof(Definition));

    public static readonly StyledProperty<IEnumerable<EquipmentCardViewModel>?> CardsProperty =
        AvaloniaProperty.Register<RouteMapAttachedCardsLayer, IEnumerable<EquipmentCardViewModel>?>(nameof(Cards));

    public static readonly StyledProperty<IBrush> PlaceholderBorderBrushProperty =
        AvaloniaProperty.Register<RouteMapAttachedCardsLayer, IBrush>(
            nameof(PlaceholderBorderBrush),
            new SolidColorBrush(Color.Parse("#C8D0D7")));

    public static readonly StyledProperty<Thickness> PlaceholderBorderThicknessProperty =
        AvaloniaProperty.Register<RouteMapAttachedCardsLayer, Thickness>(
            nameof(PlaceholderBorderThickness),
            new Thickness(2, 0, 0, 0));

    public static readonly StyledProperty<CornerRadius> PlaceholderCornerRadiusProperty =
        AvaloniaProperty.Register<RouteMapAttachedCardsLayer, CornerRadius>(
            nameof(PlaceholderCornerRadius),
            new CornerRadius(0));

    private INotifyCollectionChanged? _subscribedCards;
    private readonly List<EquipmentCardView> _cardViews = [];
    private readonly List<RouteMapAttachedCardPlaceholder> _placeholderCards = [];

    static RouteMapAttachedCardsLayer()
    {
        AffectsArrange<RouteMapAttachedCardsLayer>(DefinitionProperty, CardsProperty);
        AffectsMeasure<RouteMapAttachedCardsLayer>(CardsProperty);
    }

    public RouteMapDefinition? Definition
    {
        get => GetValue(DefinitionProperty);
        set => SetValue(DefinitionProperty, value);
    }

    public IEnumerable<EquipmentCardViewModel>? Cards
    {
        get => GetValue(CardsProperty);
        set => SetValue(CardsProperty, value);
    }

    public IBrush PlaceholderBorderBrush
    {
        get => GetValue(PlaceholderBorderBrushProperty);
        set => SetValue(PlaceholderBorderBrushProperty, value);
    }

    public Thickness PlaceholderBorderThickness
    {
        get => GetValue(PlaceholderBorderThicknessProperty);
        set => SetValue(PlaceholderBorderThicknessProperty, value);
    }

    public CornerRadius PlaceholderCornerRadius
    {
        get => GetValue(PlaceholderCornerRadiusProperty);
        set => SetValue(PlaceholderCornerRadiusProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == CardsProperty)
            RebuildChildren();

        if (change.Property == PlaceholderBorderBrushProperty ||
            change.Property == PlaceholderBorderThicknessProperty ||
            change.Property == PlaceholderCornerRadiusProperty)
            ApplyPlaceholderBorderSettings();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in _cardViews)
        {
            var width = child.DataContext is EquipmentCardViewModel card
                ? card.CardWidth
                : RouteMapAttachedCardLayout.DefaultCardWidth;
            child.Measure(new Size(width, availableSize.Height));
        }

        foreach (var placeholder in _placeholderCards)
            placeholder.Measure(new Size(RouteMapAttachedCardLayout.DefaultCardWidth, availableSize.Height));

        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var definition = Definition;
        if (definition is null)
            return ArrangeAtOrigin(finalSize);

        var placeholderBounds = new List<(Rect Bounds, RoutePlaceholderRule Rule)>();
        var transform = RouteMapViewportLayout.Create(
            definition,
            finalSize.Width,
            finalSize.Height).Transform;

        var nodesById = definition.Nodes.ToDictionary(x => x.Id);
        var segmentsById = definition.Segments.ToDictionary(x => x.Id);

        foreach (var child in _cardViews)
        {
            if (child.DataContext is not EquipmentCardViewModel card ||
                FindAttachedChain(definition, card.Id) is not { } chain)
            {
                child.Arrange(new Rect(child.DesiredSize));
                continue;
            }

            if (!card.IsVisible)
            {
                child.Arrange(default);
                continue;
            }

            var chainBounds = RouteMapBoundsCalculator.CalculateChainBounds(chain, nodesById, segmentsById);
            var chainRight = CalculateChainRightView(chain, chainBounds, nodesById, transform);
            var centerY = RouteMapAttachedCardLayout.ResolveAnchorCenterY(
                chain,
                chainBounds,
                nodesById,
                transform);
            var bounds = RouteMapAttachedCardLayout.CalculateBounds(
                finalSize.Width,
                finalSize.Height,
                chainRight,
                centerY,
                chain.AttachedCardRightOffset,
                card.CardHeight,
                card.CardWidth,
                card.MinimumCardWidth,
                (definition.Display ?? new RouteMapDisplaySettings()).CardColumnGap);

            child.Measure(new Size(bounds.Width, finalSize.Height));
            bounds = RouteMapAttachedCardLayout.CalculateBounds(
                finalSize.Width,
                finalSize.Height,
                chainRight,
                centerY,
                chain.AttachedCardRightOffset,
                card.CardHeight,
                card.CardWidth,
                card.MinimumCardWidth,
                (definition.Display ?? new RouteMapDisplaySettings()).CardColumnGap);
            child.Arrange(bounds);

            foreach (var rule in (definition.PlaceholderRules ?? []).Where(x => x.IsVisible && x.CardId == card.Id))
            {
                var placeholderHeight = rule.HeightMode == RoutePlaceholderHeightMode.MatchCard
                    ? bounds.Height
                    : rule.FixedHeight;
                placeholderBounds.AddRange(RouteMapAttachedCardLayout.CalculatePlaceholderBounds(
                        bounds,
                        finalSize.Height,
                        placeholderHeight,
                        rule.Gap,
                        rule.Placement,
                        rule.MaximumCount)
                    .Select(x => (x, rule)));
            }
        }

        SyncPlaceholderChildren(placeholderBounds.Count);

        for (var i = 0; i < placeholderBounds.Count; i++)
        {
            var bounds = placeholderBounds[i].Bounds;
            var placeholder = _placeholderCards[i];
            ApplyPlaceholderSettings(placeholder, placeholderBounds[i].Rule);
            placeholder.Measure(bounds.Size);
            placeholder.Arrange(bounds);
        }

        return finalSize;
    }

    private void RebuildChildren()
    {
        if (_subscribedCards is not null)
            _subscribedCards.CollectionChanged -= OnCardsCollectionChanged;

        _subscribedCards = Cards as INotifyCollectionChanged;
        if (_subscribedCards is not null)
            _subscribedCards.CollectionChanged += OnCardsCollectionChanged;

        Children.Clear();
        _cardViews.Clear();
        _placeholderCards.Clear();

        if (Cards is not null)
        {
            foreach (var card in Cards)
            {
                var cardView = new EquipmentCardView { DataContext = card };
                _cardViews.Add(cardView);
                Children.Add(cardView);
            }
        }

        InvalidateMeasure();
        InvalidateArrange();
    }

    private void OnCardsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildChildren();
    }

    private static RouteChain? FindAttachedChain(RouteMapDefinition definition, string cardId)
    {
        return definition.Chains.FirstOrDefault(x => x.AttachedEquipmentCardId == cardId);
    }

    private static double CalculateChainRightView(
        RouteChain chain,
        Rect chainBounds,
        IReadOnlyDictionary<string, RouteNode> nodesById,
        RouteMapTransform transform)
    {
        var geometryRight = transform.ToViewPoint(chainBounds.Right, chainBounds.Center.Y).X;
        var nodeRight = chain.NodeIds
            .Where(nodesById.ContainsKey)
            .Where(x => nodesById[x].IsVisible)
            .Select(x =>
            {
                var node = nodesById[x];
                return transform.ToViewPoint(node.X, node.Y).X + RouteMapNodeMetrics.RadiusForNode(node);
            })
            .DefaultIfEmpty(geometryRight)
            .Max();

        return Math.Max(geometryRight, nodeRight);
    }

    private Size ArrangeAtOrigin(Size finalSize)
    {
        SyncPlaceholderChildren(0);

        foreach (var child in _cardViews)
            child.Arrange(new Rect(child.DesiredSize));

        return finalSize;
    }

    internal void SyncPlaceholderChildren(int count)
    {
        while (_placeholderCards.Count < count)
        {
            var placeholder = new RouteMapAttachedCardPlaceholder();
            ApplyPlaceholderBorderSettings(placeholder);
            _placeholderCards.Add(placeholder);
            Children.Add(placeholder);
        }

        while (_placeholderCards.Count > count)
        {
            var index = _placeholderCards.Count - 1;
            var placeholder = _placeholderCards[index];
            _placeholderCards.RemoveAt(index);
            Children.Remove(placeholder);
        }
    }

    private void ApplyPlaceholderBorderSettings()
    {
        foreach (var placeholder in _placeholderCards)
            ApplyPlaceholderBorderSettings(placeholder);
    }

    private void ApplyPlaceholderBorderSettings(RouteMapAttachedCardPlaceholder placeholder)
    {
        placeholder.BorderBrush = PlaceholderBorderBrush;
        placeholder.BorderThickness = PlaceholderBorderThickness;
        placeholder.CornerRadius = PlaceholderCornerRadius;
    }

    private static void ApplyPlaceholderSettings(RouteMapAttachedCardPlaceholder placeholder, RoutePlaceholderRule rule)
    {
        var style = rule.Style ?? new RoutePlaceholderStyle();
        placeholder.Background = RouteMapPalette.Brush(style.BackgroundColor);
        placeholder.BorderBrush = RouteMapPalette.Brush(style.BorderColor);
        placeholder.BorderThickness = new Thickness(
            style.BorderThickness.Left,
            style.BorderThickness.Top,
            style.BorderThickness.Right,
            style.BorderThickness.Bottom);
        placeholder.CornerRadius = new CornerRadius(
            style.CornerRadius.TopLeft,
            style.CornerRadius.TopRight,
            style.CornerRadius.BottomRight,
            style.CornerRadius.BottomLeft);
        placeholder.Margin = new Thickness(
            style.Margin.Left,
            style.Margin.Top,
            style.Margin.Right,
            style.Margin.Bottom);
    }
}

internal sealed class RouteMapAttachedCardPlaceholder : Border
{
    public RouteMapAttachedCardPlaceholder()
    {
        Background = Brushes.Transparent;
        DataContext = null;
        IsHitTestVisible = false;
        Margin = new Thickness(RouteMapAttachedCardLayout.CardOuterMargin);
    }
}

internal static class RouteMapAttachedCardLayout
{
    public const double DefaultCardWidth = RouteMapViewportLayout.DefaultCardWidth;
    public const double MinimumCardWidth = 250;
    public const double PlaceholderGap = 10;
    public const double CardOuterMargin = 5;

    public static Rect CalculateBounds(
        double viewWidth,
        double viewHeight,
        double chainRightView,
        double chainCenterYView,
        double requestedRightOffset,
        double cardHeight)
    {
        return CalculateBounds(
            viewWidth,
            viewHeight,
            chainRightView,
            chainCenterYView,
            requestedRightOffset,
            cardHeight,
            DefaultCardWidth,
            MinimumCardWidth,
            RouteMapViewportLayout.CardGap);
    }

    public static Rect CalculateBounds(
        double viewWidth,
        double viewHeight,
        double chainRightView,
        double chainCenterYView,
        double requestedRightOffset,
        double cardHeight,
        double requestedCardWidth,
        double minimumCardWidth,
        double cardGap)
    {
        var normalizedViewWidth = Math.Max(0, viewWidth);
        var normalizedViewHeight = Math.Max(0, viewHeight);
        var rightOffset = Math.Clamp(requestedRightOffset, 0, normalizedViewWidth);
        var right = viewWidth - rightOffset;
        var minimumLeft = Math.Clamp(chainRightView + Math.Max(0, cardGap), 0, right);
        var desiredWidth = Math.Min(Math.Max(minimumCardWidth, requestedCardWidth), right);
        var left = Math.Max(minimumLeft, right - desiredWidth);
        var cardWidth = Math.Max(0, right - left);
        var normalizedHeight = Math.Min(Math.Max(0, cardHeight), normalizedViewHeight);
        var top = Math.Clamp(
            chainCenterYView - normalizedHeight / 2,
            0,
            Math.Max(0, normalizedViewHeight - normalizedHeight));

        return new Rect(left, top, cardWidth, normalizedHeight);
    }

    public static double ResolveAnchorCenterY(
        RouteChain chain,
        Rect chainBounds,
        IReadOnlyDictionary<string, RouteNode> nodesById,
        RouteMapTransform transform)
    {
        var anchor = chain.AttachedCardVerticalAnchor;
        if (anchor.Kind == RouteCardVerticalAnchorKind.Node &&
            !string.IsNullOrWhiteSpace(anchor.NodeId) &&
            chain.NodeIds.Contains(anchor.NodeId) &&
            nodesById.TryGetValue(anchor.NodeId, out var node))
            return transform.ToViewPoint(node.X, node.Y).Y;

        return transform.ToViewPoint(chainBounds.Center.X, chainBounds.Center.Y).Y;
    }

    public static IReadOnlyList<Rect> CalculatePlaceholderBounds(
        Rect mainCardBounds,
        double viewHeight,
        double placeholderHeight,
        double gap)
    {
        return CalculatePlaceholderBounds(
            mainCardBounds,
            viewHeight,
            placeholderHeight,
            gap,
            RoutePlaceholderPlacement.Both,
            maximumCount: null);
    }

    public static IReadOnlyList<Rect> CalculatePlaceholderBounds(
        Rect mainCardBounds,
        double viewHeight,
        double placeholderHeight,
        double gap,
        RoutePlaceholderPlacement placement,
        int? maximumCount)
    {
        if (mainCardBounds.Width <= 0 || placeholderHeight <= 0 || viewHeight <= 0)
            return Array.Empty<Rect>();

        var normalizedGap = Math.Max(0, gap);
        var bounds = new List<Rect>();

        if (placement is RoutePlaceholderPlacement.Above or RoutePlaceholderPlacement.Both)
        {
            for (var top = mainCardBounds.Top - normalizedGap - placeholderHeight;
                 top >= 0 && !ReachedLimit(bounds, maximumCount);
                 top -= placeholderHeight + normalizedGap)
            {
                bounds.Add(new Rect(
                    mainCardBounds.Left,
                    top,
                    mainCardBounds.Width,
                    placeholderHeight));
            }
        }

        if (placement is RoutePlaceholderPlacement.Below or RoutePlaceholderPlacement.Both)
        {
            for (var top = mainCardBounds.Bottom + normalizedGap;
                 top + placeholderHeight <= viewHeight && !ReachedLimit(bounds, maximumCount);
                 top += placeholderHeight + normalizedGap)
            {
                bounds.Add(new Rect(
                    mainCardBounds.Left,
                    top,
                    mainCardBounds.Width,
                    placeholderHeight));
            }
        }

        return bounds;
    }

    private static bool ReachedLimit(IReadOnlyCollection<Rect> bounds, int? maximumCount) =>
        maximumCount.HasValue && bounds.Count >= maximumCount.Value;

}
