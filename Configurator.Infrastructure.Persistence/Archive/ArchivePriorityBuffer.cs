using System.Threading.Channels;
using Configurator.Application.Services.Archiving;
using Microsoft.Extensions.Options;

namespace Configurator.Infrastructure.Persistence.Archive;

public sealed class ArchivePriorityBuffer
{
    private readonly Channel<ArchiveEnvelope> _criticalChannel;
    private readonly Channel<ArchiveEnvelope> _normalChannel;
    private readonly SemaphoreSlim _signal = new(0);
    private readonly object _gate = new();
    private ArchiveEnvelope? _latestTelemetry;
    private int _criticalDepth;
    private int _normalDepth;
    private bool _accepting;
    private bool _completed;
    private long _droppedTelemetryCount;

    public ArchivePriorityBuffer(IOptions<ArchiveOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var channelCapacity = Math.Max(1, options.Value.ChannelCapacity);
        var criticalCapacity = Math.Max(1, channelCapacity / 4);
        var normalCapacity = Math.Max(1, channelCapacity / 4);

        _criticalChannel = CreateWaitChannel(criticalCapacity);
        _normalChannel = CreateWaitChannel(normalCapacity);
    }

    public ArchivePriorityBufferSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                var hasTelemetry = _latestTelemetry is not null;
                var criticalDepth = Volatile.Read(ref _criticalDepth);
                var normalDepth = Volatile.Read(ref _normalDepth);
                var queueDepth = criticalDepth + normalDepth + (hasTelemetry ? 1 : 0);

                return new ArchivePriorityBufferSnapshot(
                    criticalDepth,
                    normalDepth,
                    hasTelemetry,
                    queueDepth,
                    Volatile.Read(ref _droppedTelemetryCount));
            }
        }
    }

    public void Open()
    {
        lock (_gate)
        {
            if (_completed)
            {
                throw new ObjectDisposedException(nameof(ArchivePriorityBuffer));
            }

            _accepting = true;
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            _accepting = false;
        }
    }

    public void Complete()
    {
        lock (_gate)
        {
            _accepting = false;
            _completed = true;
            _latestTelemetry = null;
        }

        _criticalChannel.Writer.TryComplete();
        _normalChannel.Writer.TryComplete();
        ReleaseSignal();
    }

    public async ValueTask<ArchiveOperationResult> EnqueueAsync(
        ArchiveEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.Priority == ArchivePriority.Telemetry)
        {
            return EnqueueTelemetry(envelope);
        }

        return envelope.Priority switch
        {
            ArchivePriority.Critical => await EnqueueChannelAsync(
                envelope,
                _criticalChannel.Writer,
                static buffer => Interlocked.Increment(ref buffer._criticalDepth),
                cancellationToken).ConfigureAwait(false),
            ArchivePriority.Normal => await EnqueueChannelAsync(
                envelope,
                _normalChannel.Writer,
                static buffer => Interlocked.Increment(ref buffer._normalDepth),
                cancellationToken).ConfigureAwait(false),
            _ => ArchiveOperationResult.Failure(
                ArchivePersistenceErrorCodes.ArchiveRecordKindUnsupported,
                "Archive envelope priority is unsupported.")
        };
    }

    public async Task WaitForDataAsync(CancellationToken cancellationToken)
    {
        if (Snapshot.QueueDepth > 0)
        {
            return;
        }

        await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<ArchiveEnvelope> Drain(int maxItems)
    {
        if (maxItems <= 0)
        {
            return Array.Empty<ArchiveEnvelope>();
        }

        var drained = new List<ArchiveEnvelope>(maxItems);
        DrainChannel(_criticalChannel.Reader, drained, maxItems, static buffer => Interlocked.Decrement(ref buffer._criticalDepth));
        DrainChannel(_normalChannel.Reader, drained, maxItems, static buffer => Interlocked.Decrement(ref buffer._normalDepth));

        if (drained.Count < maxItems)
        {
            ArchiveEnvelope? telemetry;
            lock (_gate)
            {
                telemetry = _latestTelemetry;
                _latestTelemetry = null;
            }

            if (telemetry is not null)
            {
                drained.Add(telemetry);
            }
        }

        return drained;
    }

    internal void Wake()
        => ReleaseSignal();

    private ArchiveOperationResult EnqueueTelemetry(ArchiveEnvelope envelope)
    {
        if (!CanAccept(out var failure))
        {
            return failure;
        }

        var shouldSignal = false;
        lock (_gate)
        {
            if (!CanAcceptLocked(out failure))
            {
                return failure;
            }

            if (_latestTelemetry is not null)
            {
                _droppedTelemetryCount++;
            }
            else
            {
                shouldSignal = true;
            }

            _latestTelemetry = envelope;
        }

        if (shouldSignal)
        {
            ReleaseSignal();
        }

        return ArchiveOperationResult.Success();
    }

    private async ValueTask<ArchiveOperationResult> EnqueueChannelAsync(
        ArchiveEnvelope envelope,
        ChannelWriter<ArchiveEnvelope> writer,
        Action<ArchivePriorityBuffer> incrementDepth,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (!CanAccept(out var failure))
            {
                return failure;
            }

            try
            {
                if (!await writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
                {
                    return ArchiveOperationResult.Failure(
                        ArchivePersistenceErrorCodes.ArchiveQueueClosed,
                        "Archive queue is closed.");
                }
            }
            catch (OperationCanceledException)
            {
                return ArchiveOperationResult.Failure(
                    ArchivePersistenceErrorCodes.ArchiveEnqueueCanceled,
                    "Archive enqueue was canceled.");
            }

            if (!CanAccept(out failure))
            {
                return failure;
            }

            if (writer.TryWrite(envelope))
            {
                incrementDepth(this);
                ReleaseSignal();

                return ArchiveOperationResult.Success();
            }
        }
    }

    private bool CanAccept(out ArchiveOperationResult failure)
    {
        lock (_gate)
        {
            return CanAcceptLocked(out failure);
        }
    }

    private bool CanAcceptLocked(out ArchiveOperationResult failure)
    {
        if (_completed)
        {
            failure = ArchiveOperationResult.Failure(
                ArchivePersistenceErrorCodes.ArchiveRuntimeDisposed,
                "Archive runtime is disposed.");
            return false;
        }

        if (!_accepting)
        {
            failure = ArchiveOperationResult.Failure(
                ArchivePersistenceErrorCodes.ArchiveRuntimeNotRunning,
                "Archive runtime is not running.");
            return false;
        }

        failure = ArchiveOperationResult.Success();
        return true;
    }

    private void DrainChannel(
        ChannelReader<ArchiveEnvelope> reader,
        List<ArchiveEnvelope> target,
        int maxItems,
        Action<ArchivePriorityBuffer> decrementDepth)
    {
        while (target.Count < maxItems && reader.TryRead(out var envelope))
        {
            target.Add(envelope);
            decrementDepth(this);
        }
    }

    private static Channel<ArchiveEnvelope> CreateWaitChannel(int capacity)
        => Channel.CreateBounded<ArchiveEnvelope>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    private void ReleaseSignal()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }
}
