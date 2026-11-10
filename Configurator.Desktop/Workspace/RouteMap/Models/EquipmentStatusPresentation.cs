namespace Configurator.Desktop.Workspace.RouteMap.Models;

public enum EquipmentStatusQuality
{
    Known,
    UnknownCode,
    Missing,
    BadOrStale,
    WrongType,
    Disconnected,
    ForcedFault
}

public sealed record EquipmentStatusPresentation(
    ushort? Code,
    string Text,
    string Color,
    EquipmentStatusCategory? Category,
    EquipmentStatusQuality Quality,
    bool BlockStart,
    bool BlockStop,
    bool IsTrusted,
    string DiagnosticText,
    RouteObjectState? StateOverride);
