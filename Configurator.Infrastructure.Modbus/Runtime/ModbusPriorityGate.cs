namespace Configurator.Infrastructure.Modbus.Runtime;

/// <summary>One owner at a time, with FIFO admission within each priority.</summary>
internal sealed class ModbusPriorityGate
{
    private readonly object _sync = new();
    private readonly LinkedList<TaskCompletionSource> _urgent = [];
    private readonly LinkedList<TaskCompletionSource> _normal = [];
    private bool _held;

    public bool TryEnter()
    {
        lock (_sync)
        {
            if (_held)
                return false;

            _held = true;
            return true;
        }
    }

    public Task WaitAsync(CancellationToken cancellationToken = default, bool priority = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LinkedListNode<TaskCompletionSource> waiter;
        lock (_sync)
        {
            if (!_held)
            {
                _held = true;
                return Task.CompletedTask;
            }

            waiter = (priority ? _urgent : _normal).AddLast(
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        }

        return WaitForTurnAsync(waiter, cancellationToken);
    }

    public void Release()
    {
        lock (_sync)
        {
            var next = _urgent.First ?? _normal.First;
            if (next is null)
            {
                _held = false;
                return;
            }

            next.List!.Remove(next);
            next.Value.SetResult();
        }
    }

    private async Task WaitForTurnAsync(LinkedListNode<TaskCompletionSource> waiter, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(() =>
        {
            lock (_sync)
            {
                if (waiter.List is not { } queue)
                    return;

                queue.Remove(waiter);
                waiter.Value.SetCanceled(cancellationToken);
            }
        });

        await waiter.Value.Task.ConfigureAwait(false);
    }
}
