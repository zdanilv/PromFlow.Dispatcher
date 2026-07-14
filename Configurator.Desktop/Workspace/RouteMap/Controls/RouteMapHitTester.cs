using Avalonia;
using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.Controls;

public enum RouteMapHitKind
{
    Node,
    Segment,
    Vehicle
}

public sealed record RouteMapHitResult(string ObjectId, RouteMapHitKind Kind);

public static class RouteMapHitTester
{
    public static RouteMapHitResult? HitTest(
        RouteMapDefinition definition,
        RouteMapRuntimeState runtimeState,
        Point viewPoint,
        RouteMapTransform transform)
    {
        var nodeHit = HitNodes(definition, runtimeState, viewPoint, transform);
        if (nodeHit is not null)
            return nodeHit;

        var logicalPoint = transform.ToLogicalPoint(viewPoint);
        var vehicleHit = HitVehicles(definition, runtimeState, logicalPoint);
        if (vehicleHit is not null)
            return vehicleHit;

        return null;
    }

    private static RouteMapHitResult? HitNodes(
        RouteMapDefinition definition,
        RouteMapRuntimeState runtimeState,
        Point viewPoint,
        RouteMapTransform transform)
    {
        const double hitPadding = 5;

        foreach (var node in definition.Nodes)
        {
            if (!node.IsVisible || !IsVisible(runtimeState, node.Id) || !IsEnabled(runtimeState, node.Id))
                continue;

            var distance = Distance(viewPoint, transform.ToViewPoint(node.X, node.Y));
            var hitRadius = RouteMapNodeMetrics.RadiusForNode(node) + hitPadding;
            if (distance <= hitRadius)
                return new RouteMapHitResult(node.Id, RouteMapHitKind.Node);
        }

        return null;
    }

    private static RouteMapHitResult? HitVehicles(
        RouteMapDefinition definition,
        RouteMapRuntimeState runtimeState,
        Point logicalPoint)
    {
        const double hitRadius = 24;

        foreach (var vehicle in definition.Vehicles)
        {
            if (!IsVisible(runtimeState, vehicle.Id) || !IsEnabled(runtimeState, vehicle.Id))
                continue;

            var distance = Distance(logicalPoint, new Point(vehicle.X, vehicle.Y));
            if (distance <= hitRadius)
                return new RouteMapHitResult(vehicle.Id, RouteMapHitKind.Vehicle);
        }

        return null;
    }

    private static bool IsVisible(RouteMapRuntimeState runtimeState, string objectId)
    {
        return runtimeState.Find(objectId)?.IsVisible ?? true;
    }

    private static bool IsEnabled(RouteMapRuntimeState runtimeState, string objectId)
    {
        return runtimeState.Find(objectId)?.IsEnabled ?? true;
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

}
