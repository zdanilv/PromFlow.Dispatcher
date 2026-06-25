namespace Configurator.Application.Services.Runtime;

public sealed class ApplicationRuntimeStateChangedEventArgs : EventArgs
{
    public ApplicationRuntimeStateChangedEventArgs(ApplicationRuntimeSnapshot previous, ApplicationRuntimeSnapshot current)
    {
        Previous = previous;
        Current = current;
    }

    public ApplicationRuntimeSnapshot Previous { get; }

    public ApplicationRuntimeSnapshot Current { get; }
}
