using Configurator.Application.Services.Archiving;

namespace Configurator.Infrastructure.Persistence.Archive;

public sealed class ArchiveHealthService : IArchiveHealthService
{
    private readonly object _gate = new();
    private readonly List<IObserver<ArchiveHealth>> _observers = [];
    private ArchiveHealth _current = ArchiveHealth.Stopped;

    public ArchiveHealth Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public IObservable<ArchiveHealth> Observe()
        => new ArchiveHealthObservable(this);

    public void Publish(ArchiveHealth health)
    {
        ArgumentNullException.ThrowIfNull(health);

        IObserver<ArchiveHealth>[] observers;
        lock (_gate)
        {
            _current = health;
            observers = _observers.ToArray();
        }

        foreach (var observer in observers)
        {
            observer.OnNext(health);
        }
    }

    private IDisposable Subscribe(IObserver<ArchiveHealth> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        ArchiveHealth current;
        lock (_gate)
        {
            _observers.Add(observer);
            current = _current;
        }

        observer.OnNext(current);

        return new Subscription(this, observer);
    }

    private void Unsubscribe(IObserver<ArchiveHealth> observer)
    {
        lock (_gate)
        {
            _observers.Remove(observer);
        }
    }

    private sealed class ArchiveHealthObservable(ArchiveHealthService owner) : IObservable<ArchiveHealth>
    {
        public IDisposable Subscribe(IObserver<ArchiveHealth> observer)
            => owner.Subscribe(observer);
    }

    private sealed class Subscription(ArchiveHealthService owner, IObserver<ArchiveHealth> observer) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Unsubscribe(observer);
            }
        }
    }
}
