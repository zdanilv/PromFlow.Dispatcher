namespace Configurator.Desktop.Workspace.RouteMap.Models;

public sealed record RouteMapRuntimeState(
    IReadOnlyDictionary<string, RouteObjectRuntimeState> Objects,
    bool IsAutomaticMode,
    bool IsManualMode,
    bool HasEmergency,
    bool IsQueueRunning,
    string ConnectionStatusText)
{
    public static RouteMapRuntimeState Empty { get; } = new(
        new Dictionary<string, RouteObjectRuntimeState>(),
        IsAutomaticMode: false,
        IsManualMode: true,
        HasEmergency: false,
        IsQueueRunning: false,
        ConnectionStatusText: "Ожидание");

    public RouteObjectRuntimeState? Find(string objectId)
    {
        return Objects.TryGetValue(objectId, out var state)
            ? state
            : null;
    }
}

public sealed record RouteObjectRuntimeState(
    string ObjectId,
    RouteObjectState State,
    string? Text,
    string? ValueText,
    bool IsVisible,
    bool CanStart,
    bool CanStop,
    bool IsStartChecked = false,
    bool IsStopChecked = false,
    bool IsSignalActive = false,
    bool? IsLoader = null,
    bool? IsTarget = null);
