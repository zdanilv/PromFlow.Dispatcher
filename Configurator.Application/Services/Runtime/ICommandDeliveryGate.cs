using Configurator.Application.Services.Archiving;

namespace Configurator.Application.Services.Runtime;

public interface ICommandDeliveryGate
{
    CommandDeliveryDecision Evaluate(CommandExecutionContext context);
}
