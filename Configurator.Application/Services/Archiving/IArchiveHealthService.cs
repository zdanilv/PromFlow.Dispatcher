namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Exposes current archive health and health changes.
/// </summary>
public interface IArchiveHealthService
{
    ArchiveHealth Current { get; }

    IObservable<ArchiveHealth> Observe();
}
