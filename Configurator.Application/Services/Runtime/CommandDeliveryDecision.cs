namespace Configurator.Application.Services.Runtime;

public sealed record CommandDeliveryDecision(
    bool Allowed,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static CommandDeliveryDecision Allow()
        => new(true, null, null);

    public static CommandDeliveryDecision Deny(string errorCode, string errorMessage)
        => new(false, errorCode, errorMessage);
}
