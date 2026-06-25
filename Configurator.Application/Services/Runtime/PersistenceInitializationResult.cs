namespace Configurator.Application.Services.Runtime;

public sealed record PersistenceInitializationResult(
    IReadOnlyList<ApplicationRuntimeStepResult> Steps)
{
    public bool HasFatalFailure => Steps.Any(step => !step.Succeeded && step.IsFatal);
}
