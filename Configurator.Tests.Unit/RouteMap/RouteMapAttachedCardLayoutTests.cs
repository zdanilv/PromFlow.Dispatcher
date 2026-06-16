using Avalonia;
using Avalonia.Media;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Panels;
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class RouteMapAttachedCardLayoutTests
{
    [Fact]
    public void CalculateBounds_places_card_at_right_edge_when_offset_is_zero()
    {
        var bounds = RouteMapAttachedCardLayout.CalculateBounds(
            viewWidth: 1000,
            viewHeight: 700,
            chainRightView: 650,
            chainCenterYView: 250,
            requestedRightOffset: 0,
            cardHeight: 141);

        Assert.Equal(295, bounds.Width);
        Assert.Equal(1000, bounds.Right);
        Assert.Equal(705, bounds.Left);
    }

    [Fact]
    public void CalculateBounds_moves_card_left_by_positive_right_offset()
    {
        var bounds = RouteMapAttachedCardLayout.CalculateBounds(
            viewWidth: 1000,
            viewHeight: 700,
            chainRightView: 600,
            chainCenterYView: 250,
            requestedRightOffset: 40,
            cardHeight: 141);

        Assert.Equal(960, bounds.Right);
        Assert.Equal(665, bounds.Left);
    }

    [Fact]
    public void CalculateBounds_shrinks_card_before_it_overlaps_chain()
    {
        var bounds = RouteMapAttachedCardLayout.CalculateBounds(
            viewWidth: 1000,
            viewHeight: 700,
            chainRightView: 700,
            chainCenterYView: 250,
            requestedRightOffset: 120,
            cardHeight: 141);

        Assert.Equal(718, bounds.Left);
        Assert.Equal(880, bounds.Right);
    }

    [Fact]
    public void CalculateBounds_centers_card_on_chain_y_after_transform()
    {
        var definition = Configurator.Desktop.Workspace.RouteMap.Models.RouteMapSeed.Create();
        var chain = definition.Chains.Single();
        var transform = RouteMapViewportLayout.Create(definition, 1440, 700).Transform;
        var node = definition.Nodes.Single(x => x.Id == "concrete_bucket");
        var chainCenterY = transform.ToViewPoint(node.X, node.Y).Y;

        var bounds = RouteMapAttachedCardLayout.CalculateBounds(
            viewWidth: 1440,
            viewHeight: 700,
            chainRightView: 1100,
            chainCenterYView: chainCenterY,
            requestedRightOffset: 0,
            cardHeight: 141);

        Assert.Equal(chainCenterY, bounds.Center.Y);
    }

    [Fact]
    public void CalculateBounds_clamps_card_to_small_viewport()
    {
        var bounds = RouteMapAttachedCardLayout.CalculateBounds(
            viewWidth: 220,
            viewHeight: 100,
            chainRightView: 180,
            chainCenterYView: 10,
            requestedRightOffset: 0,
            cardHeight: 141);

        Assert.Equal(0, bounds.Top);
        Assert.Equal(100, bounds.Height);
        Assert.InRange(bounds.Left, 0, 220);
        Assert.InRange(bounds.Right, 0, 220);
    }

    [Fact]
    public void ResolveAnchorCenterY_uses_selected_chain_node()
    {
        var definition = Configurator.Desktop.Workspace.RouteMap.Models.RouteMapSeed.Create();
        var chain = definition.Chains.Single();
        var nodes = definition.Nodes.ToDictionary(x => x.Id);
        var segments = definition.Segments.ToDictionary(x => x.Id);
        var chainBounds = RouteMapBoundsCalculator.CalculateChainBounds(chain, nodes, segments);
        var transform = RouteMapViewportLayout.Create(definition, 1440, 700).Transform;

        var centerY = RouteMapAttachedCardLayout.ResolveAnchorCenterY(chain, chainBounds, nodes, transform);

        Assert.Equal(transform.ToViewPoint(nodes["concrete_bucket"].X, nodes["concrete_bucket"].Y).Y, centerY);
    }

    [Theory]
    [InlineData(RouteCardVerticalAnchorKind.ChainBoundsCenter, null)]
    [InlineData(RouteCardVerticalAnchorKind.Node, "missing")]
    public void ResolveAnchorCenterY_falls_back_to_chain_bounds_center(
        RouteCardVerticalAnchorKind kind,
        string? nodeId)
    {
        var definition = Configurator.Desktop.Workspace.RouteMap.Models.RouteMapSeed.Create();
        var original = definition.Chains.Single();
        var chain = original with { AttachedCardVerticalAnchor = new RouteCardVerticalAnchor(kind, nodeId) };
        var nodes = definition.Nodes.ToDictionary(x => x.Id);
        var segments = definition.Segments.ToDictionary(x => x.Id);
        var chainBounds = RouteMapBoundsCalculator.CalculateChainBounds(chain, nodes, segments);
        var transform = RouteMapViewportLayout.Create(definition, 1440, 700).Transform;

        var centerY = RouteMapAttachedCardLayout.ResolveAnchorCenterY(chain, chainBounds, nodes, transform);

        Assert.Equal(transform.ToViewPoint(chainBounds.Center.X, chainBounds.Center.Y).Y, centerY);
    }

    [Fact]
    public void CalculatePlaceholderBounds_places_placeholders_above_and_below_main_card()
    {
        var mainCardBounds = new Rect(705, 379.5, 295, 141);

        var placeholders = RouteMapAttachedCardLayout.CalculatePlaceholderBounds(
            mainCardBounds,
            viewHeight: 900,
            placeholderHeight: mainCardBounds.Height,
            gap: 10);

        var above = placeholders.Where(x => x.Bottom <= mainCardBounds.Top).ToArray();
        var below = placeholders.Where(x => x.Top >= mainCardBounds.Bottom).ToArray();

        Assert.Equal(2, above.Length);
        Assert.Equal(2, below.Length);
        Assert.Equal(228.5, above[0].Top);
        Assert.Equal(77.5, above[1].Top);
        Assert.Equal(530.5, below[0].Top);
        Assert.Equal(681.5, below[1].Top);
    }

    [Fact]
    public void CalculatePlaceholderBounds_uses_same_left_width_and_height_as_main_card()
    {
        var mainCardBounds = new Rect(705, 379.5, 295, 141);

        var placeholders = RouteMapAttachedCardLayout.CalculatePlaceholderBounds(
            mainCardBounds,
            viewHeight: 900,
            placeholderHeight: mainCardBounds.Height,
            gap: RouteMapAttachedCardLayout.PlaceholderGap);

        Assert.All(placeholders, placeholder =>
        {
            Assert.Equal(mainCardBounds.Left, placeholder.Left);
            Assert.Equal(mainCardBounds.Width, placeholder.Width);
            Assert.Equal(mainCardBounds.Height, placeholder.Height);
        });
    }

    [Fact]
    public void CalculatePlaceholderBounds_skips_partially_clipped_placeholders()
    {
        var mainCardBounds = new Rect(705, 100, 295, 141);

        var placeholders = RouteMapAttachedCardLayout.CalculatePlaceholderBounds(
            mainCardBounds,
            viewHeight: 350,
            placeholderHeight: mainCardBounds.Height,
            gap: 10);

        Assert.Empty(placeholders);
    }

    [Fact]
    public void AttachedCardsLayer_uses_default_placeholder_border_settings()
    {
        var layer = new RouteMapAttachedCardsLayer();

        var brush = Assert.IsType<SolidColorBrush>(layer.PlaceholderBorderBrush);
        Assert.Equal(Color.Parse("#C8D0D7"), brush.Color);
        Assert.Equal(new Thickness(2, 0, 0, 0), layer.PlaceholderBorderThickness);
        Assert.Equal(new CornerRadius(0), layer.PlaceholderCornerRadius);
    }

    [Fact]
    public void AttachedCardsLayer_accepts_placeholder_border_thickness_for_each_side()
    {
        var layer = new RouteMapAttachedCardsLayer
        {
            PlaceholderBorderThickness = new Thickness(0, 1, 2, 1),
        };

        Assert.Equal(new Thickness(0, 1, 2, 1), layer.PlaceholderBorderThickness);
    }

    [Fact]
    public void AttachedCardsLayer_applies_placeholder_border_settings_to_created_placeholders()
    {
        var customBrush = new SolidColorBrush(Color.Parse("#123456"));
        var layer = new RouteMapAttachedCardsLayer
        {
            PlaceholderBorderBrush = customBrush,
            PlaceholderBorderThickness = new Thickness(0, 1, 2, 1),
            PlaceholderCornerRadius = new CornerRadius(3),
        };

        layer.SyncPlaceholderChildren(2);

        var placeholders = layer.Children.OfType<RouteMapAttachedCardPlaceholder>().ToArray();
        Assert.Equal(2, placeholders.Length);
        Assert.All(placeholders, placeholder =>
        {
            Assert.Same(customBrush, placeholder.BorderBrush);
            Assert.Equal(new Thickness(0, 1, 2, 1), placeholder.BorderThickness);
            Assert.Equal(new CornerRadius(3), placeholder.CornerRadius);
            Assert.False(placeholder.IsHitTestVisible);
            Assert.Null(placeholder.DataContext);
        });
    }

    [Fact]
    public void AttachedCardsLayer_updates_existing_placeholders_when_border_settings_change()
    {
        var layer = new RouteMapAttachedCardsLayer();
        layer.SyncPlaceholderChildren(2);
        var placeholders = layer.Children.OfType<RouteMapAttachedCardPlaceholder>().ToArray();
        Assert.Equal(2, placeholders.Length);
        var updatedBrush = new SolidColorBrush(Color.Parse("#654321"));

        layer.PlaceholderBorderBrush = updatedBrush;
        layer.PlaceholderBorderThickness = new Thickness(1, 2, 3, 4);
        layer.PlaceholderCornerRadius = new CornerRadius(5);

        Assert.All(placeholders, placeholder =>
        {
            Assert.Same(updatedBrush, placeholder.BorderBrush);
            Assert.Equal(new Thickness(1, 2, 3, 4), placeholder.BorderThickness);
            Assert.Equal(new CornerRadius(5), placeholder.CornerRadius);
        });
    }
}
