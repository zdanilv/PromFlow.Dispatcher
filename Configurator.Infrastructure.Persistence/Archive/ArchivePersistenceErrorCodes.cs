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
    public const string ArchiveQueryInvalid = nameof(ArchiveQueryInvalid);
    public const string ArchiveQueryFailed = nameof(ArchiveQueryFailed);
    public const string ArchiveExportInvalid = nameof(ArchiveExportInvalid);
    public const string ArchiveExportLimitExceeded = nameof(ArchiveExportLimitExceeded);
    public const string ArchiveExportFailed = nameof(ArchiveExportFailed);
    public const string ArchiveRetentionFailed = nameof(ArchiveRetentionFailed);
    public const string ArchiveBackupInvalid = nameof(ArchiveBackupInvalid);
    public const string ArchiveBackupFailed = nameof(ArchiveBackupFailed);
    public const string ArchiveBackupIntegrityFailed = nameof(ArchiveBackupIntegrityFailed);
    public const string ArchivePartitionCorrupt = nameof(ArchivePartitionCorrupt);
    public const string ArchivePartitionMissing = nameof(ArchivePartitionMissing);
}
