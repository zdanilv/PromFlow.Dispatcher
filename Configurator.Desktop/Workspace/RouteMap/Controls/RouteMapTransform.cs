using Avalonia;

namespace Configurator.Desktop.Workspace.RouteMap.Controls;

public sealed class RouteMapTransform
{
    private RouteMapTransform(double scale, double offsetX, double offsetY)
    {
        Scale = scale;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    public double Scale { get; }
    public double OffsetX { get; }
    public double OffsetY { get; }

    public static RouteMapTransform Create(
        double logicalWidth,
        double logicalHeight,
        double viewWidth,
        double viewHeight,
        double padding = 18)
    {
        var safeLogicalWidth = Math.Max(1, logicalWidth);
        var safeLogicalHeight = Math.Max(1, logicalHeight);
        var safeViewWidth = Math.Max(1, viewWidth - padding * 2);
        var safeViewHeight = Math.Max(1, viewHeight - padding * 2);

        var scale = Math.Min(safeViewWidth / safeLogicalWidth, safeViewHeight / safeLogicalHeight);
        var offsetX = (viewWidth - safeLogicalWidth * scale) / 2;
        var offsetY = (viewHeight - safeLogicalHeight * scale) / 2;

        return new RouteMapTransform(scale, offsetX, offsetY);
    }

    public static RouteMapTransform CreateForBounds(
        Rect logicalBounds,
        Rect viewBounds,
        double padding = 18)
    {
        return CreateForBounds(logicalBounds, viewBounds, padding, padding);
    }

    public static RouteMapTransform CreateForBounds(
        Rect logicalBounds,
        Rect viewBounds,
        double horizontalPadding,
        double verticalPadding)
    {
        var safeLogicalWidth = Math.Max(1, logicalBounds.Width);
        var safeLogicalHeight = Math.Max(1, logicalBounds.Height);
        var safeViewWidth = Math.Max(1, viewBounds.Width - horizontalPadding * 2);
        var safeViewHeight = Math.Max(1, viewBounds.Height - verticalPadding * 2);
        var scale = Math.Min(safeViewWidth / safeLogicalWidth, safeViewHeight / safeLogicalHeight);
        var offsetX = viewBounds.X + (viewBounds.Width - safeLogicalWidth * scale) / 2 - logicalBounds.X * scale;
        var offsetY = viewBounds.Y + (viewBounds.Height - safeLogicalHeight * scale) / 2 - logicalBounds.Y * scale;

        return new RouteMapTransform(scale, offsetX, offsetY);
    }

    public static RouteMapTransform CreateForLogicalCanvas(
        Rect routeBounds,
        double logicalWidth,
        double logicalHeight,
        Rect viewBounds,
        double horizontalPadding,
        double verticalPadding)
    {
        var layoutWidth = Math.Max(Math.Max(1, logicalWidth), routeBounds.Width);
        var layoutHeight = Math.Max(Math.Max(1, logicalHeight), routeBounds.Height);
        var safeViewWidth = Math.Max(1, viewBounds.Width - horizontalPadding * 2);
        var safeViewHeight = Math.Max(1, viewBounds.Height - verticalPadding * 2);
        var scale = Math.Min(safeViewWidth / layoutWidth, safeViewHeight / layoutHeight);
        var offsetX = viewBounds.Center.X - routeBounds.Center.X * scale;
        var offsetY = viewBounds.Center.Y - routeBounds.Center.Y * scale;

        return new RouteMapTransform(scale, offsetX, offsetY);
    }

    public Point ToViewPoint(double x, double y)
    {
        return new Point(OffsetX + x * Scale, OffsetY + y * Scale);
    }

    public Point ToLogicalPoint(Point point)
    {
        return new Point(
            (point.X - OffsetX) / Scale,
            (point.Y - OffsetY) / Scale);
    }
}
