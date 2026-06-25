namespace Configurator.Application.Services.Runtime;

public sealed record ApplicationRuntimeStepResult(
    string Step,
    bool Succeeded,
    bool IsFatal,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static ApplicationRuntimeStepResult Success(string step)
        => new(step, true, false, null, null);

    public static ApplicationRuntimeStepResult Failure(
        string step,
        string errorCode,
        string errorMessage,
        bool isFatal)
        => new(step, false, isFatal, errorCode, errorMessage);
}
