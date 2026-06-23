namespace Configurator.Infrastructure.Persistence.Archive;

public static class ArchivePersistenceErrorCodes
{
    public const string ArchiveDisabled = nameof(ArchiveDisabled);
    public const string ArchiveOptionsInvalid = nameof(ArchiveOptionsInvalid);
    public const string ArchiveRuntimeNotRunning = nameof(ArchiveRuntimeNotRunning);
    public const string ArchiveRuntimeDisposed = nameof(ArchiveRuntimeDisposed);
    public const string ArchiveQueueClosed = nameof(ArchiveQueueClosed);
    public const string ArchiveEnqueueCanceled = nameof(ArchiveEnqueueCanceled);
    public const string ArchiveRecordKindUnsupported = nameof(ArchiveRecordKindUnsupported);
    public const string ArchiveRecordTypeMismatch = nameof(ArchiveRecordTypeMismatch);
    public const string ArchiveWriteFailed = nameof(ArchiveWriteFailed);
    public const string ArchiveFlushFailed = nameof(ArchiveFlushFailed);
    public const string ArchiveStartFailed = nameof(ArchiveStartFailed);
    public const string ArchiveStopFailed = nameof(ArchiveStopFailed);
}
