namespace Configurator.Infrastructure.OpcUa.Common;

/// <summary>
/// Хранит текущее значение и уведомляет подписчиков IObservable о каждом изменении.
/// </summary>
internal sealed class ObservableValue<T> : IObservable<T>
{
    private readonly object _lock = new();
    private readonly List<IObserver<T>> _observers = [];
    private T? _current;
    private bool _hasCurrent;

    /// <summary>
    /// Создает observable-хранилище с начальным значением.
    /// </summary>
    public ObservableValue()
    {
    }

    /// <summary>
    /// Создает observable-хранилище с начальным значением.
    /// </summary>
    public ObservableValue(T initialValue)
    {
        _current = initialValue;
        _hasCurrent = true;
    }

    /// <summary>
    /// Подписывает наблюдателя и сразу отправляет ему текущее значение.
    /// </summary>
    public T Current
        => _hasCurrent
            ? _current!
            : throw new InvalidOperationException("No value has been published yet.");

    /// <summary>
    /// Добавляет наблюдателя и возвращает подписку для отмены.
    /// </summary>
    public IDisposable Subscribe(IObserver<T> observer)
    {
        lock (_lock)
        {
            _observers.Add(observer);
            if (_hasCurrent)
            {
                observer.OnNext(_current!);
            }
        }

        return new Subscription(this, observer);
    }

    /// <summary>
    /// Сохраняет новое значение и отправляет его всем подписчикам.
    /// </summary>
    public void Publish(T value)
    {
        IObserver<T>[] observers;

        lock (_lock)
        {
            _current = value;
            _hasCurrent = true;
            observers = [.. _observers];
        }

        foreach (var observer in observers)
        {
            observer.OnNext(value);
        }
    }

    private void Unsubscribe(IObserver<T> observer)
    {
        lock (_lock)
        {
            _observers.Remove(observer);
        }
    }

    /// <summary>
    /// Хранит связь наблюдателя с ObservableValue и отписывает его при Dispose.
    /// </summary>
    private sealed class Subscription : IDisposable
    {
        private readonly ObservableValue<T> _owner;
        private readonly IObserver<T> _observer;
        private bool _disposed;

        /// <summary>
        /// Создает объект подписки, удаляющий наблюдателя при Dispose.
        /// </summary>
        public Subscription(ObservableValue<T> owner, IObserver<T> observer)
        {
            _owner = owner;
            _observer = observer;
        }

        /// <summary>
        /// Освобождает ресурс и предотвращает повторное выполнение связанного действия.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _owner.Unsubscribe(_observer);
            _disposed = true;
        }
    }
}
