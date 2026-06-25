namespace Configurator.Application.Services.Runtime;

public interface IApplicationRuntimeStateAccessor
{
    ApplicationRuntimeSnapshot Current { get; }

    event EventHandler<ApplicationRuntimeStateChangedEventArgs>? StateChanged;
}
