namespace Configurator.Application.Services.Runtime;

public interface IApplicationInstanceLease : IAsyncDisposable
{
    string Description { get; }
}
