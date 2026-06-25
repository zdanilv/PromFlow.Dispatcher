using Configurator.Application.Services.Archiving;

namespace Configurator.Application.Services.Runtime;

public sealed class RuntimeCommandDeliveryGate : ICommandDeliveryGate
{
    public const string ShuttingDownErrorCode = "ApplicationShuttingDown";

    private readonly IApplicationRuntimeStateAccessor _stateAccessor;

    public RuntimeCommandDeliveryGate(IApplicationRuntimeStateAccessor stateAccessor)
    {
        _stateAccessor = stateAccessor ?? throw new ArgumentNullException(nameof(stateAccessor));
    }

    public CommandDeliveryDecision Evaluate(CommandExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.IsEmergency)
        {
            return CommandDeliveryDecision.Allow();
        }

        return _stateAccessor.Current.IsShuttingDown
            ? CommandDeliveryDecision.Deny(
                ShuttingDownErrorCode,
                "Application is shutting down; non-emergency commands are rejected.")
            : CommandDeliveryDecision.Allow();
    }
}
