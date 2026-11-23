using System.Globalization;
using Configurator.Application.Services.History;
using Microsoft.Extensions.Logging;

namespace Configurator.Infrastructure.Services.History;

public sealed class HistoryArchiveService : IHistoryArchiveService
{
    private readonly SqliteHistoryEventStore _store;
    private readonly HistoryEventWriter _writer;
    private readonly ILogger<HistoryArchiveService> _logger;
    private readonly TimeProvider _clock;
    private readonly HistoryArchiveFileOperations _files;
    private readonly SemaphoreSlim _archiveGate = new(1, 1);
    private readonly object _lifecycleGate = new();
    private readonly object _statusGate = new();
    private readonly object _scheduleGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationTokenSource _operations = new();
    private volatile HistoryArchiveOptions _options;
    private HistoryArchiveStatus _status = new();
    private Task? _starting;
    private Task? _shutdown;
    private Task? _scheduler;
    private volatile bool _ready;
    private volatile bool _stopping;
    private bool _recovered;
    private volatile bool _optionsValid;
    private long _scheduleTimestamp;
    private TimeSpan _scheduleDelay;
    private DateTimeOffset _lastLogUtc;

    public HistoryArchiveService(SqliteHistoryEventStore store, HistoryEventWriter writer,
        HistoryArchiveOptions options, ILogger<HistoryArchiveService> logger,
        TimeProvider? clock = null, HistoryArchiveFileOperations? files = null)
    {
        _store = store;
        _writer = writer;
        _options = options with { };
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
        _files = files ?? new();
        _writer.Changed += OnWriterChanged;
    }

    public HistoryArchiveStatus CurrentStatus { get { lock (_statusGate) return _status; } }
    public HistoryArchiveOptions CurrentOptions => _options with { };
    public event EventHandler<HistoryArchiveStatus>? StatusChanged;

    public Task StartAsync(CancellationToken ct = default)
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            return _starting ??= Task.Run(async () =>
            {
                using var startup = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
                await InitializeAsync(startup.Token);
                _scheduler = Task.Run(() => ScheduleAsync(_lifetime.Token));
            });
        }
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        try
        {
            await _store.InitializeAsync(ct);
            await _writer.StartAsync(ct);
            _ready = true;
            Publish(s => s with { StorageReady = true });
            try { HistoryArchivePaths.Validate(_options); }
            catch (ArgumentException ex) { Fail("HistorySettings", ex); return; }
            _optionsValid = true;
            var previousCheck = await _store.GetMetadataAsync("lastScheduledCheckUtc", ct);
            var delay = TimeSpan.FromMinutes(_options.ArchiveIntervalMinutes);
            if (DateTimeOffset.TryParse(previousCheck, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var previous) && previous <= _clock.GetUtcNow())
                delay = previous + delay - _clock.GetUtcNow();
            else
                await _store.SetMetadataAsync("lastScheduledCheckUtc", _clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture), ct);
            ResetSchedule(delay < TimeSpan.Zero ? TimeSpan.Zero : delay);
            var cursor = await _store.GetLastArchivedIdAsync(ct);
            Publish(s => s with { State = IdleState, LastArchivedEventId = cursor,
                LastErrorCode = null, LastErrorMessage = null });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { Fail("HistoryStorage", ex); }
    }

    private HistoryArchiveState IdleState => _options.Enabled ? HistoryArchiveState.Running : HistoryArchiveState.Disabled;

    public async Task ValidateOptionsAsync(HistoryArchiveOptions options, CancellationToken ct = default)
    {
        HistoryArchivePaths.Validate(options);
        var directory = HistoryArchivePaths.GetDirectory(options);
        Directory.CreateDirectory(directory);
        var probe = Path.Combine(directory, ".promflow-history-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var file = _files.Create(probe);
            await file.WriteAsync(new byte[] { 0 }, ct);
            await file.FlushAsync(ct);
        }
        finally { if (File.Exists(probe)) _files.Delete(probe); }
    }

    public async Task ApplyOptionsAsync(HistoryArchiveOptions options, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_stopping, this);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, _operations.Token);
        ct = operation.Token;
        await ValidateOptionsAsync(options, ct);
        await _archiveGate.WaitAsync(ct);
        try
        {
            var delay = TimeSpan.FromMinutes(options.ArchiveIntervalMinutes);
            var previousCheck = _store.IsInitialized ? await _store.GetMetadataAsync("lastScheduledCheckUtc", ct) : null;
            if (DateTimeOffset.TryParse(previousCheck, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var previous) && previous <= _clock.GetUtcNow())
                delay = previous + delay - _clock.GetUtcNow();
            _options = options with { };
            _optionsValid = true;
            ResetSchedule(delay < TimeSpan.Zero ? TimeSpan.Zero : delay);
            Publish(s => s with { State = _ready ? IdleState : s.State,
                LastErrorCode = _writer.LastErrorCode, LastErrorMessage = _writer.LastErrorMessage });
        }
        finally { _archiveGate.Release(); }
    }

    public async Task<ArchiveResult> ArchiveNowAsync(CancellationToken ct = default)
    {
        if (!_ready || !_optionsValid || _stopping) return new(ArchiveOutcome.Unavailable, Message: CurrentStatus.LastErrorMessage);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, _operations.Token);
        ct = operation.Token;
        if (!await _archiveGate.WaitAsync(0, ct)) return new(ArchiveOutcome.AlreadyRunning);
        try { return await Task.Run(() => ArchiveCoreAsync(ct), ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Fail("HistoryArchive", ex);
            return new(ArchiveOutcome.Error, Message: ex.Message);
        }
        finally { _archiveGate.Release(); }
    }

    private async Task<ArchiveResult> ArchiveCoreAsync(CancellationToken ct)
    {
        Publish(s => s with { State = HistoryArchiveState.Archiving, LastArchiveAttemptUtc = _clock.GetUtcNow() });
        await RecoverAsync(ct);
        using (var flushBudget = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            flushBudget.CancelAfter(TimeSpan.FromSeconds(5));
            await _writer.FlushAsync(flushBudget.Token);
        }
        var cursor = await _store.GetLastArchivedIdAsync(ct);
        var maximum = await _store.GetMaximumIdAsync(ct);
        if (maximum <= cursor)
        {
            await RetainAsync(ct);
            Publish(s => s with { State = IdleState, LastErrorCode = null, LastErrorMessage = null });
            return new(ArchiveOutcome.NoNewEvents);
        }

        var options = _options;
        var directory = HistoryArchivePaths.GetDirectory(options);
        Directory.CreateDirectory(directory);
        if (_files.GetAvailableBytes(directory) / (1024 * 1024) < options.MinimumFreeSpaceMb)
            throw new IOException("Недостаточно свободного места для архива истории.");
        var sequenceText = await _store.GetMetadataAsync("archiveSequence", ct);
        _ = long.TryParse(sequenceText, CultureInfo.InvariantCulture, out var sequence);
        sequence++;
        await _store.SetMetadataAsync("archiveSequence", sequence.ToString(CultureInfo.InvariantCulture), ct);
        var createdUtc = _clock.GetUtcNow();
        var reservedPaths = (await _store.GetArchiveRunsAsync(ct)).Select(r => r.ArchivePath);
        var finalPath = HistoryArchivePaths.GetUniquePath(options, createdUtc, sequence, reservedPaths);
        var temporaryPath = Path.Combine(directory, "." + Path.GetFileName(finalPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        var intent = new SqliteArchiveIntent(Guid.NewGuid().ToString("N"), temporaryPath, finalPath,
            cursor + 1, maximum, maximum - cursor, "", createdUtc);
        await _store.SaveArchiveIntentAsync(intent, ct);
        HistoryArchiveManifest manifest;
        await using (var output = _files.Create(temporaryPath))
            manifest = await HistoryZipArchive.WriteAsync(output, _store.ReadRangeAsync(cursor, maximum, ct), createdUtc, cursor + 1, maximum, ct);
        var verified = await HistoryZipArchive.VerifyAsync(temporaryPath, ct);
        if (verified != manifest) throw new InvalidDataException("Архив изменился после записи.");
        intent = intent with { EventsSha256 = manifest.EventsSha256 };
        await _store.SaveArchiveIntentAsync(intent, ct);
        _files.Move(temporaryPath, finalPath);
        _files.Published(finalPath);
        await _store.RegisterArchiveAsync(ToRun(finalPath, manifest), cursor, ct);
        PublishSuccess(finalPath, manifest);
        try { await RetainAsync(ct); }
        catch (Exception ex)
        {
            Fail("HistoryRetention", ex);
            return new(ArchiveOutcome.Success, finalPath, ex.Message);
        }
        return new(ArchiveOutcome.Success, finalPath);
    }

    private SqliteArchiveRun ToRun(string path, HistoryArchiveManifest manifest) =>
        new(manifest.FirstEventId, manifest.LastEventId, manifest.EventCount, path, manifest.EventsSha256,
            manifest.CreatedUtc, _clock.GetUtcNow());

    private async Task RecoverAsync(CancellationToken ct, bool auditRuns = false)
    {
        var intents = await _store.GetArchiveIntentsAsync(ct);
        foreach (var intent in intents.OrderBy(i => i.FirstEventId))
        {
            var temporaryExists = ArchiveFileExists(intent.TemporaryPath);
            if (!ArchiveFileExists(intent.ArchivePath))
            {
                if (temporaryExists)
                    _files.Delete(intent.TemporaryPath);
                else if (intent.EventsSha256.Length > 0)
                    throw new IOException("Не найден проверенный архив на стадии публикации. Проверьте доступность диска с архивами.");
                await _store.RemoveArchiveIntentAsync(intent.IntentId, ct);
                continue;
            }
            if (temporaryExists)
                throw new IOException("Обнаружена коллизия окончательного имени незавершённого архива.");
            var manifest = await HistoryZipArchive.VerifyAsync(intent.ArchivePath, ct);
            var cursor = await _store.GetLastArchivedIdAsync(ct);
            if (manifest.FirstEventId != cursor + 1 || manifest.FirstEventId != intent.FirstEventId ||
                manifest.LastEventId != intent.LastEventId || manifest.EventCount != intent.EventCount ||
                !string.Equals(manifest.EventsSha256, intent.EventsSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Незавершённый архив конфликтует с сохранённым диапазоном истории.");
            await _store.RegisterArchiveAsync(ToRun(intent.ArchivePath, manifest), cursor, ct);
            PublishSuccess(intent.ArchivePath, manifest);
        }
        var runs = await _store.GetArchiveRunsAsync(ct);
        var lastRun = runs.OrderByDescending(r => r.LastEventId).FirstOrDefault();
        if (lastRun is not null)
            Publish(s => s with { LastArchivedEventId = lastRun.LastEventId, LastSuccessfulArchivePath = lastRun.ArchivePath,
                LastSuccessfulArchiveUtc = lastRun.CreatedUtc, LastArchiveEventCount = lastRun.EventCount });
        foreach (var run in runs)
        {
            if (run.Expired) continue;
            var expiryIntent = await _store.GetMetadataAsync("expiryPending_" + run.LastEventId.ToString(CultureInfo.InvariantCulture), ct);
            if (!ArchiveFileExists(run.ArchivePath))
            {
                if (expiryIntent == run.EventsSha256)
                {
                    await _store.MarkArchiveExpiredAsync(run.ArchivePath, ct);
                    continue;
                }
                throw new FileNotFoundException("Подтверждённый архив истории отсутствует.", run.ArchivePath);
            }
            if (auditRuns)
            {
                var manifest = await HistoryZipArchive.VerifyAsync(run.ArchivePath, ct);
                ValidateRun(run, manifest);
                if (lastRun == run) Publish(s => s with { LastArchiveSizeBytes = new FileInfo(run.ArchivePath).Length });
            }
        }
    }

    private static bool ArchiveFileExists(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.Directory) == 0; }
        catch (FileNotFoundException) { return false; }
    }

    private static void ValidateRun(SqliteArchiveRun run, HistoryArchiveManifest manifest)
    {
        if (run.FirstEventId != manifest.FirstEventId || run.LastEventId != manifest.LastEventId ||
            run.EventCount != manifest.EventCount || !string.Equals(run.EventsSha256, manifest.EventsSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Метаданные архива не совпадают с базой истории.");
    }

    private async Task RetainAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var options = _options;
        var localCutoff = RetentionCutoff(now, options.LocalDatabaseRetentionDays);
        var archiveCutoff = RetentionCutoff(now, options.RetentionDays);
        foreach (var run in await _store.GetArchiveRunsAsync(ct))
        {
            var zipExpired = options.RetentionDays > 0 && run.CreatedUtc < archiveCutoff;
            var localExpired = !run.Expired && await _store.HasArchivedEventsBeforeAsync(run.FirstEventId, run.LastEventId,
                localCutoff, ct);
            if (localExpired || zipExpired && !run.Expired)
            {
                var manifest = await HistoryZipArchive.VerifyAsync(run.ArchivePath, ct);
                ValidateRun(run, manifest);
            }
            if (localExpired)
            {
                while (await _store.DeleteArchivedEventsAsync(run.FirstEventId, run.LastEventId, localCutoff, 1000, ct) > 0) { }
            }
            if (zipExpired && !run.Expired)
            {
                await _store.SetMetadataAsync("expiryPending_" + run.LastEventId.ToString(CultureInfo.InvariantCulture), run.EventsSha256, ct);
                _files.Delete(run.ArchivePath);
                await _store.MarkArchiveExpiredAsync(run.ArchivePath, ct);
            }
        }
    }

    private static DateTimeOffset RetentionCutoff(DateTimeOffset now, int days) =>
        days >= (now - DateTimeOffset.MinValue).TotalDays ? DateTimeOffset.MinValue : now.AddDays(-days);

    private void PublishSuccess(string path, HistoryArchiveManifest manifest) => Publish(s => s with
    {
        State = IdleState, LastSuccessfulArchiveUtc = manifest.CreatedUtc, LastSuccessfulArchivePath = path,
        LastArchivedEventId = manifest.LastEventId, LastArchiveEventCount = manifest.EventCount,
        LastArchiveSizeBytes = new FileInfo(path).Length, LastErrorCode = null, LastErrorMessage = null
    });

    private void ResetSchedule(TimeSpan delay)
    {
        lock (_scheduleGate)
        {
            _scheduleTimestamp = _clock.GetTimestamp();
            _scheduleDelay = delay;
        }
        Publish(s => s with { NextArchiveDueUtc = _options.Enabled ? _clock.GetUtcNow() + delay : null });
    }

    private bool ScheduleIsDue()
    {
        lock (_scheduleGate) return _clock.GetElapsedTime(_scheduleTimestamp) >= _scheduleDelay;
    }

    private async Task<ArchiveResult> ArchiveScheduledAsync(CancellationToken ct)
    {
        if (!await _archiveGate.WaitAsync(0, ct)) return new(ArchiveOutcome.AlreadyRunning);
        try
        {
            if (!_options.Enabled || !ScheduleIsDue()) return new(ArchiveOutcome.AlreadyRunning);
            return await ArchiveCoreAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { Fail("HistoryArchive", ex); return new(ArchiveOutcome.Error, Message: ex.Message); }
        finally { _archiveGate.Release(); }
    }

    private async Task ScheduleAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!_ready) await InitializeAsync(ct);
                if (_ready && _optionsValid && !_recovered)
                {
                    await _archiveGate.WaitAsync(ct);
                    try { await RecoverAsync(ct, auditRuns: true); _recovered = true; }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex) { Fail("HistoryRecovery", ex); }
                    finally { _archiveGate.Release(); }
                }
                if (_ready && _optionsValid && _options.Enabled && ScheduleIsDue())
                {
                    var result = await ArchiveScheduledAsync(ct);
                    if (result.Outcome != ArchiveOutcome.AlreadyRunning)
                    {
                        try { await _store.SetMetadataAsync("lastScheduledCheckUtc", _clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture), ct); }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                        catch (Exception ex) { Fail("HistoryScheduleMetadata", ex); }
                        ResetSchedule(result.Outcome == ArchiveOutcome.Error ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(_options.ArchiveIntervalMinutes));
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(5), _clock, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Fail("HistoryScheduler", ex);
            if (!_stopping)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), _clock, ct);
                _scheduler = Task.Run(() => ScheduleAsync(ct), CancellationToken.None);
            }
        }
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        lock (_lifecycleGate)
        {
            _stopping = true;
            return _shutdown ??= Task.Run(() => StopCoreAsync(_starting, ct));
        }
    }

    private async Task StopCoreAsync(Task? startup, CancellationToken ct)
    {
        await _operations.CancelAsync();
        await _lifetime.CancelAsync();
        if (startup is not null)
        {
            try { await startup; }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception ex) { Fail("HistoryStartup", ex); }
        }
        using var writerBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        writerBudget.CancelAfter(TimeSpan.FromSeconds(6));
        var writerStop = _writer.StopAsync(writerBudget.Token);
        try
        {
            if (_scheduler is not null) await _scheduler.WaitAsync(ct);
            await writerStop;
            if (_ready && _optionsValid && _options.Enabled && _options.ArchiveOnExit)
            {
                using var archiveBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                archiveBudget.CancelAfter(TimeSpan.FromSeconds(4));
                await _archiveGate.WaitAsync(archiveBudget.Token);
                try { await Task.Run(() => ArchiveCoreAsync(archiveBudget.Token), archiveBudget.Token); }
                finally { _archiveGate.Release(); }
            }
            Publish(s => s with
            {
                State = HistoryArchiveState.Stopped, StorageReady = false, NextArchiveDueUtc = null,
                PersistedEventsCount = _writer.PersistedEventsCount, RejectedEventsCount = _writer.RejectedEventsCount,
                PendingQueueCount = _writer.PendingQueueCount
            });
        }
        catch (Exception ex) { Fail("HistoryShutdown", ex); throw; }
        finally
        {
            _writer.Changed -= OnWriterChanged;
            // Cancellation has been delivered to every public operation before releasing the exclusive storage lock.
            await _archiveGate.WaitAsync(CancellationToken.None);
            try { await _store.DisposeAsync(); }
            finally { _archiveGate.Release(); }
            Publish(s => s with { StorageReady = false });
        }
    }

    private void OnWriterChanged(object? sender, EventArgs e) => Publish(s => s with
    {
        PersistedEventsCount = _writer.PersistedEventsCount, RejectedEventsCount = _writer.RejectedEventsCount,
        PendingQueueCount = _writer.PendingQueueCount,
        State = _writer.LastErrorCode is null ? s.State : HistoryArchiveState.Error,
        LastErrorCode = _writer.LastErrorCode ?? s.LastErrorCode,
        LastErrorMessage = _writer.LastErrorMessage ?? s.LastErrorMessage
    });

    private void Fail(string code, Exception exception)
    {
        var state = exception is HistoryStorageInUseException
            ? HistoryArchiveState.StorageInUse : HistoryArchiveState.Error;
        Publish(s => s with { State = state, LastErrorCode = code, LastErrorMessage = exception.Message });
        if (_clock.GetUtcNow() - _lastLogUtc >= TimeSpan.FromSeconds(30))
        {
            _logger.LogError(exception, "History subsystem failure {ErrorCode}", code);
            _lastLogUtc = _clock.GetUtcNow();
        }
    }

    private void Publish(Func<HistoryArchiveStatus, HistoryArchiveStatus> update)
    {
        HistoryArchiveStatus status;
        lock (_statusGate)
        {
            status = update(_status);
            if (_writer.RejectedEventsCount > 0 && status.State is HistoryArchiveState.Running or HistoryArchiveState.Disabled)
                status = status with { State = HistoryArchiveState.Error, LastErrorCode = status.LastErrorCode ?? "HistoryRejected",
                    LastErrorMessage = status.LastErrorMessage ?? "Часть событий не принята. Проверьте счётчик непринятых событий." };
            _status = status;
        }
        StatusChanged?.Invoke(this, status);
    }
}
