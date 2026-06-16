using Avalonia;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class RouteMapTransformTests
{
    [Fact]
    public void Create_preserves_aspect_ratio_and_round_trips_points()
    {
        var transform = RouteMapTransform.Create(
            logicalWidth: 1200,
            logicalHeight: 760,
            viewWidth: 1920,
            viewHeight: 1080,
            padding: 0);

        Assert.Equal(1080d / 760d, transform.Scale, precision: 8);

        var viewPoint = transform.ToViewPoint(600, 380);
        var logicalPoint = transform.ToLogicalPoint(viewPoint);

        Assert.Equal(600, logicalPoint.X, precision: 8);
        Assert.Equal(380, logicalPoint.Y, precision: 8);
    }
}
