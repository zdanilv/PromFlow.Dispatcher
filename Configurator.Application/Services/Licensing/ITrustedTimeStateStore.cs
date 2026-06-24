namespace Configurator.Application.Services.Licensing;

public interface ITrustedTimeStateStore
{
    Task<TrustedTimeState?> ReadAsync(CancellationToken cancellationToken = default);

    Task WriteAsync(TrustedTimeState state, CancellationToken cancellationToken = default);
}
