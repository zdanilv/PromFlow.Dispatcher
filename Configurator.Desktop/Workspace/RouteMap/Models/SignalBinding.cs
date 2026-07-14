using Configurator.Application.Services.Signals;

namespace Configurator.Desktop.Workspace.RouteMap.Models;

public enum SignalBindingRole
{
    State,
    Text,
    Value,
    Visible,
    StartCommand,
    StopCommand,
    UncheckedCommand,
    CheckedCommand,
    Fault,
    ActiveRoute,
    ActiveRouteFragment,
    TargetCommand,
    LoaderCommand,
    AutomaticModeCommand,
    ManualModeCommand,
    ResetCommand,
    EmergencyCommand,
    StartOffFeedback,
    StopOffFeedback,
    TargetOffFeedback,
    LoaderOffFeedback,
    AutomaticModeOffFeedback,
    ManualModeOffFeedback,
    EmergencyOffFeedback,
    EquipmentParameter,
    Enabled
}

public enum SignalBindingDirection
{
    Read,
    Write,
    ReadWrite
}

public sealed record SignalBinding(
    SignalBindingRole Role,
    string SignalId,
    SignalBindingDirection Direction,
    SignalValueType ValueType);
