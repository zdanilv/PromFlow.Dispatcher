namespace Configurator.Application.Services.Runtime;

public sealed record ApplicationRuntimeSnapshot(
    ApplicationRuntimePhase Phase,
    IReadOnlyList<ApplicationRuntimeStepResult> StartupSteps,
    IReadOnlyList<ApplicationRuntimeStepResult> ShutdownSteps)
{
    public static ApplicationRuntimeSnapshot Initial { get; } = new(
        ApplicationRuntimePhase.Stopped,
        Array.Empty<ApplicationRuntimeStepResult>(),
        Array.Empty<ApplicationRuntimeStepResult>());

    public bool IsRunning => Phase == ApplicationRuntimePhase.Running;

    public bool IsShuttingDown => Phase == ApplicationRuntimePhase.ShuttingDown;
}
