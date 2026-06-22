namespace Configurator.Infrastructure.Persistence.Common;

public enum PersistenceHealthState
{
    NotInitialized,
    Initializing,
    Ready,
    Degraded,
    Faulted
}
