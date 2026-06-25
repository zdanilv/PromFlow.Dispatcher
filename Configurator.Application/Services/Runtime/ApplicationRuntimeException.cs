namespace Configurator.Application.Services.Runtime;

public sealed class ApplicationRuntimeException : Exception
{
    public ApplicationRuntimeException(
        string message,
        IReadOnlyList<ApplicationRuntimeStepResult> stepResults,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StepResults = stepResults ?? throw new ArgumentNullException(nameof(stepResults));
    }

    public IReadOnlyList<ApplicationRuntimeStepResult> StepResults { get; }
}
