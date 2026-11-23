using System.Threading.Channels;
using Configurator.Application.Services.History;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Configurator.Infrastructure.Services.History;

/// <summary>Nonblocking bounded ingress. A failed batch stays owned by the single consumer until committed.</summary>
public sealed class HistoryEventWriter : IHistoryEventSink, IAsyncDisposable
{
    private readonly SqliteHistoryEventStore _store;
    private readonly ILogger<HistoryEventWriter> _logger;
    private readonly TimeProvider _clock;
    private readonly Channel<HistoryEvent> _queue;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _abort = new();
    private Task? _worker;
    private bool _accepting;
    private bool _stopped;
    private long _accepted;
    private long _completed;
    private long _persisted;
    private long _rejected;
    private int _pending;
    private string? _lastErrorCode;
    private string? _lastErrorMessage;
    private long _lastLogTimestamp;
    private int _notificationPending;
    private int _logPending;
    private int _disposeRequested;

    public HistoryEventWriter(SqliteHistoryEventStore store, ILogger<HistoryEventWriter>? logger = null,
        int capacity = 10_000, TimeProvider? clock = null)
    {
        _store = store;
        _logger = logger ?? NullLogger<HistoryEventWriter>.Instance;
        _clock = clock ?? TimeProvider.System;
        _queue = Channel.CreateBounded<HistoryEvent>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    }

    public long PersistedEventsCount => Interlocked.Read(ref _persisted);
    public long RejectedEventsCount => Interlocked.Read(ref _rejected);
    public int PendingQueueCount => Volatile.Read(ref _pending);
    public string? LastErrorCode => Volatile.Read(ref _lastErrorCode);
    public string? LastErrorMessage => Volatile.Read(ref _lastErrorMessage);
    public bool IsRunning => _worker is { IsCompleted: false };
    public event EventHandler? Changed;

    public Task StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_stopped) throw new InvalidOperationException("Остановленный writer истории нельзя запустить повторно.");
            if (_worker != null) return Task.CompletedTask;
            _accepting = true;
            _worker = Task.Run(() => RunAsync(_abort.Token), CancellationToken.None);
        }
        return Task.CompletedTask;
    }

    public bool TryWrite(HistoryEvent historyEvent)
    {
        ArgumentNullException.ThrowIfNull(historyEvent);
        lock (_sync)
        {
            if (_accepting)
            {
                Interlocked.Increment(ref _pending);
                if (_queue.Writer.TryWrite(historyEvent))
                {
                    Interlocked.Increment(ref _accepted);
                    return true;
                }
                Interlocked.Decrement(ref _pending);
            }
            Interlocked.Increment(ref _rejected);
        }
        ReportError("QueueRejected", "Очередь истории заполнена или запись остановлена.");
        return false;
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        var target = Interlocked.Read(ref _accepted);
        try
        {
            while (Interlocked.Read(ref _completed) < target)
            {
                ct.ThrowIfCancellationRequested();
                if (_worker is null || _worker.IsCompleted)
                    throw new IOException("Writer истории остановлен до завершения записи.");
                await Task.Delay(TimeSpan.FromMilliseconds(10), _clock, ct);
            }
        }
        catch (OperationCanceledException)
        {
            ReportError("FlushTimeout", "Не завершён сброс очереди истории в пределах времени ожидания.");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        Task? worker;
        lock (_sync)
        {
            _accepting = false;
            _stopped = true;
            _queue.Writer.TryComplete();
            worker = _worker;
        }
        if (worker is null) return;
        try { await worker.WaitAsync(ct); }
        catch (OperationCanceledException)
        {
            ReportError("FlushTimeout", "Остановка записи истории не завершена в пределах времени ожидания.");
            _abort.Cancel();
            throw;
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var batch = new List<HistoryEvent>(200);
        try
        {
            while (true)
            {
                try
                {
                    await _store.InitializeAsync(ct);
                    if (batch.Count == 0)
                    {
                        if (!await _queue.Reader.WaitToReadAsync(ct)) break;
                        while (batch.Count < 200 && _queue.Reader.TryRead(out var item)) batch.Add(item);
                    }
                    var count = await _store.AppendBatchAsync(batch, ct);
                    Interlocked.Add(ref _persisted, count);
                    Interlocked.Add(ref _completed, batch.Count);
                    Interlocked.Add(ref _pending, -batch.Count);
                    batch.Clear();
                    Volatile.Write(ref _lastErrorCode, null);
                    Volatile.Write(ref _lastErrorMessage, null);
                    PublishChanged();
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    ReportError(ex is HistoryStorageInUseException ? "StorageInUse" : "DatabaseWrite", ex.Message, ex);
                    await Task.Delay(TimeSpan.FromSeconds(1), _clock, ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { PublishChanged(); }
    }

    private void ReportError(string code, string message, Exception? exception = null)
    {
        Volatile.Write(ref _lastErrorCode, code);
        Volatile.Write(ref _lastErrorMessage, message);
        var now = _clock.GetTimestamp();
        var previous = Interlocked.Read(ref _lastLogTimestamp);
        if (previous == 0 || _clock.GetElapsedTime(previous, now) >= TimeSpan.FromSeconds(30))
        {
            if (Interlocked.CompareExchange(ref _lastLogTimestamp, now, previous) == previous &&
                Interlocked.Exchange(ref _logPending, 1) == 0)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        _logger.LogError(exception, "History writer failure {ErrorCode}. Pending {Pending}, rejected {Rejected}",
                            code, PendingQueueCount, RejectedEventsCount);
                    }
                    finally { Interlocked.Exchange(ref _logPending, 0); }
                });
            }
        }
        PublishChanged();
    }

    private void PublishChanged()
    {
        // Coalesce bursts, including rejected ingress, so observers cannot enqueue unbounded UI work.
        if (Interlocked.Exchange(ref _notificationPending, 1) != 0) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            Interlocked.Exchange(ref _notificationPending, 0);
            NotifyObservers();
        });
    }

    private void NotifyObservers()
    {
        var handlers = Changed;
        if (handlers is null) return;
        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try { handler(this, EventArgs.Empty); }
            catch (Exception ex) { _logger.LogWarning(ex, "History status observer failed"); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) != 0) return;
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await StopAsync(budget.Token); }
        catch (OperationCanceledException) { }
        if (_worker is not null) await _worker;
        _abort.Dispose();
    }
}
