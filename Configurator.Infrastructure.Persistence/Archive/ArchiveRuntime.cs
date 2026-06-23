using Configurator.Application.Services.Archiving;
using Microsoft.Extensions.Options;

namespace Configurator.Infrastructure.Persistence.Archive;

public sealed class ArchiveRuntime : IArchiveRuntime
{
    private readonly ArchivePriorityBuffer _buffer;
    private readonly SqliteArchiveWriter _writer;
    private readonly ArchiveHealthService _healthService;
    private readonly ArchiveBackoffPolicy _backoffPolicy;
    private readonly ArchiveOptionsValidator _optionsValidator;
    private readonly IOptions<ArchiveOptions> _options;
    private readonly SemaphoreSlim _flushSignal = new(0);
    private readonly object _stateGate = new();
    private Task? _workerTask;
    private CancellationTokenSource? _workerCancellation;
    private TaskCompletionSource<ArchiveOperationResult>? _pendingFlush;
    private RuntimeState _state = RuntimeState.Stopped;

    public ArchiveRuntime(
        ArchivePriorityBuffer buffer,
        SqliteArchiveWriter writer,
        ArchiveHealthService healthService,
        ArchiveBackoffPolicy backoffPolicy,
        ArchiveOptionsValidator optionsValidator,
        IOptions<ArchiveOptions> options)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _healthService = healthService ?? throw new ArgumentNullException(nameof(healthService));
        _backoffPolicy = backoffPolicy ?? throw new ArgumentNullException(nameof(backoffPolicy));
        _optionsValidator = optionsValidator ?? throw new ArgumentNullException(nameof(optionsValidator));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<ArchiveOperationResult> StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = _options.Value.Clone();
        if (!options.Enabled)
        {
            PublishHealth(ArchiveHealthState.Stopped, "Archive disabled.");
            return Task.FromResult(ArchiveOperationResult.Success());
        }

        var validation = _optionsValidator.Validate(options);
        if (!validation.Succeeded)
        {
            var details = string.Join("; ", validation.Errors.Select(error => $"{error.Code}:{error.PropertyName}"));
            PublishHealth(
                ArchiveHealthState.Faulted,
                "Archive options are invalid.",
                ArchivePersistenceErrorCodes.ArchiveOptionsInvalid,
                details);

            return Task.FromResult(ArchiveOperationResult.Failure(
                ArchivePersistenceErrorCodes.ArchiveOptionsInvalid,
                "Archive options are invalid.",
                details));
        }

        lock (_stateGate)
        {
            if (_state == RuntimeState.Disposed)
            {
                return Task.FromResult(ArchiveOperationResult.Failure(
                    ArchivePersistenceErrorCodes.ArchiveRuntimeDisposed,
                    "Archive runtime is disposed."));
            }

            if (_state == RuntimeState.Running)
            {
                return Task.FromResult(ArchiveOperationResult.Success());
            }

            _buffer.Open();
            _workerCancellation = new CancellationTokenSource();
            _workerTask = Task.Run(
                () => WorkerLoopAsync(options, _workerCancellation.Token),
                CancellationToken.None);
            _state = RuntimeState.Running;
        }

        PublishHealth(ArchiveHealthState.Healthy, "Archive running.", lastSuccessAtUtc: DateTimeOffset.UtcNow);

        return Task.FromResult(ArchiveOperationResult.Success());
    }

    public async Task<ArchiveOperationResult> FlushAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<ArchiveOperationResult> flushRequest;

        lock (_stateGate)
        {
            if (_state == RuntimeState.Disposed)
            {
                return ArchiveOperationResult.Failure(
                    ArchivePersistenceErrorCodes.ArchiveRuntimeDisposed,
                    "Archive runtime is disposed.");
            }

            if (_state != RuntimeState.Running)
            {
                return _buffer.Snapshot.QueueDepth == 0
                    ? ArchiveOperationResult.Success()
                    : ArchiveOperationResult.Failure(
                        ArchivePersistenceErrorCodes.ArchiveRuntimeNotRunning,
                        "Archive runtime is not running.");
            }

            _pendingFlush ??= new TaskCompletionSource<ArchiveOperationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            flushRequest = _pendingFlush;
        }

        _buffer.Wake();
        _flushSignal.Release();

        try
        {
            return await flushRequest.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ArchiveOperationResult.Failure(
                ArchivePersistenceErrorCodes.ArchiveFlushFailed,
                "Archive flush was canceled.");
        }
    }

    public async Task<ArchiveOperationResult> StopAsync(CancellationToken cancellationToken = default)
    {
        Task? workerTask;
        CancellationTokenSource? workerCancellation;

        lock (_stateGate)
        {
            if (_state == RuntimeState.Disposed)
            {
                return ArchiveOperationResult.Failure(
                    ArchivePersistenceErrorCodes.ArchiveRuntimeDisposed,
                    "Archive runtime is disposed.");
            }

            if (_state != RuntimeState.Running)
            {
                PublishHealth(ArchiveHealthState.Stopped, "Archive stopped.");
                return ArchiveOperationResult.Success();
            }
        }

        _buffer.Pause();
        var flushResult = await FlushAsync(cancellationToken).ConfigureAwait(false);
        if (!flushResult.Succeeded)
        {
            _buffer.Open();
            PublishHealth(
                ArchiveHealthState.Faulted,
                "Archive stop failed.",
                flushResult.ErrorCode,
                flushResult.ErrorMessage);

            return ArchiveOperationResult.Failure(
                ArchivePersistenceErrorCodes.ArchiveStopFailed,
                "Archive stop failed.",
                flushResult.ErrorMessage);
        }

        lock (_stateGate)
        {
            workerTask = _workerTask;
            workerCancellation = _workerCancellation;
            _workerTask = null;
            _workerCancellation = null;
            _state = RuntimeState.Stopped;
        }

        workerCancellation?.Cancel();
        _buffer.Wake();
        _flushSignal.Release();

        if (workerTask is not null)
        {
            try
            {
                await workerTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return ArchiveOperationResult.Failure(
                    ArchivePersistenceErrorCodes.ArchiveStopFailed,
                    "Archive stop was canceled.");
            }
        }

        workerCancellation?.Dispose();
        PublishHealth(ArchiveHealthState.Stopped, "Archive stopped.");

        return ArchiveOperationResult.Success();
    }

    public async ValueTask DisposeAsync()
    {
        bool shouldDispose;
        lock (_stateGate)
        {
            shouldDispose = _state != RuntimeState.Disposed;
        }

        if (!shouldDispose)
        {
            return;
        }

        await StopAsync(CancellationToken.None).ConfigureAwait(false);

        lock (_stateGate)
        {
            _state = RuntimeState.Disposed;
            _pendingFlush = null;
        }

        _buffer.Complete();
        _flushSignal.Release();
        await _writer.DisposeAsync().ConfigureAwait(false);
        PublishHealth(ArchiveHealthState.Stopped, "Archive disposed.");
    }

    private async Task WorkerLoopAsync(ArchiveOptions options, CancellationToken cancellationToken)
    {
        var batchSize = Math.Max(1, options.BatchSize);
        var flushInterval = TimeSpan.FromMilliseconds(Math.Max(1, options.BatchFlushIntervalMs));
        var batch = new List<ArchiveEnvelope>(batchSize);
        DateTimeOffset? batchStartedAtUtc = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                DrainInto(batch, batchSize);
                batchStartedAtUtc ??= batch.Count > 0 ? DateTimeOffset.UtcNow : null;

                var flushRequest = TakeFlushRequest();
                if (flushRequest is not null)
                {
                    var result = await FlushAvailableAsync(batch, batchSize, cancellationToken).ConfigureAwait(false);
                    CompleteFlushRequest(flushRequest, result);
                    batchStartedAtUtc = batch.Count > 0 ? DateTimeOffset.UtcNow : null;
                    continue;
                }

                if (batch.Count >= batchSize || IsFlushIntervalElapsed(batchStartedAtUtc, flushInterval))
                {
                    var result = await WriteWithRetryAsync(batch, cancellationToken).ConfigureAwait(false);
                    if (result.Succeeded)
                    {
                        batch.Clear();
                        batchStartedAtUtc = null;
                    }
                    else
                    {
                        batch.Clear();
                        batchStartedAtUtc = null;
                    }

                    continue;
                }

                await WaitForWorkAsync(batchStartedAtUtc, flushInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            PublishHealth(
                ArchiveHealthState.Faulted,
                "Archive worker failed.",
                ArchivePersistenceErrorCodes.ArchiveWriteFailed,
                ex.Message);
        }
        finally
        {
            var pendingFlush = TakeFlushRequest();
            pendingFlush?.TrySetResult(ArchiveOperationResult.Failure(
                ArchivePersistenceErrorCodes.ArchiveFlushFailed,
                "Archive worker stopped before flush completed."));
        }
    }

    private async Task<ArchiveOperationResult> FlushAvailableAsync(
        List<ArchiveEnvelope> batch,
        int batchSize,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            DrainInto(batch, batchSize);
            if (batch.Count > 0)
            {
                var writeResult = await WriteWithRetryAsync(batch, cancellationToken).ConfigureAwait(false);
                if (!writeResult.Succeeded)
                {
                    batch.Clear();
                    return writeResult;
                }

                batch.Clear();
            }

            if (_buffer.Snapshot.QueueDepth == 0)
            {
                return ArchiveOperationResult.Success();
            }
        }
    }

    private async Task<ArchiveOperationResult> WriteWithRetryAsync(
        IReadOnlyList<ArchiveEnvelope> batch,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return ArchiveOperationResult.Success();
        }

        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await _writer.WriteBatchAsync(batch, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
            {
                PublishHealth(
                    ArchiveHealthState.Healthy,
                    "Archive batch persisted.",
                    lastSuccessAtUtc: DateTimeOffset.UtcNow);

                return ArchiveOperationResult.Success();
            }

            if (!IsRetryableWriteFailure(result.ErrorCode))
            {
                PublishHealth(
                    ArchiveHealthState.Faulted,
                    "Archive batch cannot be persisted.",
                    result.ErrorCode ?? ArchivePersistenceErrorCodes.ArchiveWriteFailed,
                    result.ErrorMessage);

                return ArchiveOperationResult.Failure(
                    result.ErrorCode ?? ArchivePersistenceErrorCodes.ArchiveWriteFailed,
                    result.ErrorMessage ?? "Archive batch cannot be persisted.",
                    result.ErrorDetails);
            }

            failures++;
            PublishHealth(
                ArchiveHealthState.Degraded,
                "Archive batch write failed; retrying.",
                result.ErrorCode ?? ArchivePersistenceErrorCodes.ArchiveWriteFailed,
                result.ErrorMessage);

            try
            {
                await Task.Delay(_backoffPolicy.GetDelay(failures), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return ArchiveOperationResult.Failure(
            ArchivePersistenceErrorCodes.ArchiveWriteFailed,
            "Archive batch write was canceled.");
    }

    private void DrainInto(List<ArchiveEnvelope> batch, int batchSize)
    {
        if (batch.Count >= batchSize)
        {
            return;
        }

        var drained = _buffer.Drain(batchSize - batch.Count);
        batch.AddRange(drained);
    }

    private async Task WaitForWorkAsync(
        DateTimeOffset? batchStartedAtUtc,
        TimeSpan flushInterval,
        CancellationToken cancellationToken)
    {
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var dataTask = _buffer.WaitForDataAsync(waitCancellation.Token);
        var flushTask = _flushSignal.WaitAsync(waitCancellation.Token);
        var timerTask = batchStartedAtUtc is null
            ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
            : Task.Delay(GetRemainingFlushDelay(batchStartedAtUtc.Value, flushInterval), cancellationToken);

        var completedTask = await Task.WhenAny(dataTask, flushTask, timerTask).ConfigureAwait(false);
        await waitCancellation.CancelAsync().ConfigureAwait(false);
        await ObserveCanceledAsync(dataTask).ConfigureAwait(false);
        await ObserveCanceledAsync(flushTask).ConfigureAwait(false);
        await completedTask.ConfigureAwait(false);
    }

    private TaskCompletionSource<ArchiveOperationResult>? TakeFlushRequest()
    {
        lock (_stateGate)
        {
            var flushRequest = _pendingFlush;
            _pendingFlush = null;
            return flushRequest;
        }
    }

    private static void CompleteFlushRequest(
        TaskCompletionSource<ArchiveOperationResult> flushRequest,
        ArchiveOperationResult result)
        => flushRequest.TrySetResult(result);

    private static bool IsFlushIntervalElapsed(DateTimeOffset? batchStartedAtUtc, TimeSpan flushInterval)
        => batchStartedAtUtc is not null && DateTimeOffset.UtcNow - batchStartedAtUtc.Value >= flushInterval;

    private static TimeSpan GetRemainingFlushDelay(DateTimeOffset batchStartedAtUtc, TimeSpan flushInterval)
    {
        var elapsed = DateTimeOffset.UtcNow - batchStartedAtUtc;
        return elapsed >= flushInterval ? TimeSpan.Zero : flushInterval - elapsed;
    }

    private static bool IsRetryableWriteFailure(string? errorCode)
        => errorCode is not ArchivePersistenceErrorCodes.ArchiveRecordKindUnsupported
            and not ArchivePersistenceErrorCodes.ArchiveRecordTypeMismatch
            and not ArchivePersistenceErrorCodes.ArchiveOptionsInvalid;

    private static async Task ObserveCanceledAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void PublishHealth(
        ArchiveHealthState state,
        string message,
        string? errorCode = null,
        string? errorMessage = null,
        DateTimeOffset? lastSuccessAtUtc = null)
    {
        var snapshot = _buffer.Snapshot;
        _healthService.Publish(new ArchiveHealth(
            state,
            DateTimeOffset.UtcNow,
            message,
            snapshot.QueueDepth,
            snapshot.DroppedTelemetryCount,
            lastSuccessAtUtc,
            errorCode,
            errorMessage));
    }

    private enum RuntimeState
    {
        Stopped,
        Running,
        Disposed
    }
}
