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
    Fault,
    ActiveRoute,
    TargetCommand,
    LoaderCommand,
    AutomaticModeCommand,
    ManualModeCommand,
    EmergencyCommand,
    StartOffFeedback,
    StopOffFeedback,
    TargetOffFeedback,
    LoaderOffFeedback,
    AutomaticModeOffFeedback,
    ManualModeOffFeedback,
    EmergencyOffFeedback
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
