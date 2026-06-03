namespace Configurator.Infrastructure.OpcUa.Common;

/// <summary>
/// Выполняет переданное действие один раз при освобождении подписки или временного ресурса.
/// </summary>
internal sealed class DisposableAction : IDisposable
{
    private readonly Action _dispose;
    private bool _disposed;

    /// <summary>
    /// Сохраняет действие, которое нужно выполнить при освобождении ресурса.
    /// </summary>
    public DisposableAction(Action dispose)
    {
        _dispose = dispose;
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

        _dispose();
        _disposed = true;
    }
}
