namespace Configurator.Application.Services.Runtime;

public enum ApplicationRuntimePhase
{
    Stopped,
    Starting,
    Running,
    ShuttingDown,
    Faulted
}
