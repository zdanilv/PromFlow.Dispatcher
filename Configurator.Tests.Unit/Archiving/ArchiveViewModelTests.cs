using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Desktop.Workspace.Archive;
using Microsoft.Extensions.Options;
using System.Reactive.Threading.Tasks;
using Xunit;

namespace Configurator.Tests.Unit.Archiving;

public sealed class ArchiveViewModelTests
{
    [Fact]
    public async Task InitializeAsync_LoadsOnlyFirstSnapshotMetadataPage()
    {
        var query = new RecordingArchiveQueryService();
        using var viewModel = CreateViewModel(query);

        await viewModel.InitializeAsync();

        Assert.Equal(1, query.MetadataQueryCount);
        Assert.Equal(0, query.RawSnapshotQueryCount);
        Assert.Single(viewModel.Snapshots);
        Assert.Equal(1, viewModel.PageNumber);
        Assert.True(viewModel.HasMore);
    }

    [Fact]
    public async Task LoadSnapshotDetails_LoadsDecodedSnapshotOnlyAfterExplicitCommand()
    {
        var query = new RecordingArchiveQueryService();
        using var viewModel = CreateViewModel(query);
        await viewModel.InitializeAsync();

        viewModel.SelectedSnapshot = viewModel.Snapshots.Single();
        await viewModel.LoadSnapshotDetailsCommand.Execute().ToTask();

        Assert.Equal(1, query.DetailsQueryCount);
        Assert.NotEmpty(viewModel.SnapshotDetails.Values);
    }

    [Fact]
    public async Task ExportCommand_UsesCurrentBoundedFilterAndReportsResult()
    {
        var maintenance = new RecordingArchiveMaintenanceService();
        var picker = new RecordingArchiveFilePicker { ExportDirectory = "exports" };
        using var viewModel = CreateViewModel(
            new RecordingArchiveQueryService(),
            maintenance,
            picker);

        await viewModel.ExportCommand.Execute().ToTask();

        Assert.Equal(1, maintenance.ExportCount);
        Assert.Contains("export.zip", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    private static ArchiveViewModel CreateViewModel(
        RecordingArchiveQueryService query,
        IArchiveMaintenanceService? maintenance = null,
        IArchiveFilePicker? picker = null)
        => new(
            query,
            new RecordingArchiveHealthService(),
            maintenance ?? new RecordingArchiveMaintenanceService(),
            picker ?? new RecordingArchiveFilePicker(),
            Options.Create(new ArchiveOptions { QueryMaxPageSize = 10 }));

    private sealed class RecordingArchiveQueryService : IArchiveQueryService
    {
        private readonly Guid _snapshotId = Guid.NewGuid();
        private readonly DateTimeOffset _capturedAtUtc = new(2026, 1, 2, 3, 0, 0, TimeSpan.Zero);

        public int MetadataQueryCount { get; private set; }
        public int RawSnapshotQueryCount { get; private set; }
        public int DetailsQueryCount { get; private set; }

        public Task<ArchiveOperationResult<ArchivePage<RawModbusSnapshotArchiveMetadataRecord>>> QueryRawSnapshotMetadataAsync(
            ArchiveQuery query,
            CancellationToken cancellationToken = default)
        {
            MetadataQueryCount++;
            var row = new RawModbusSnapshotArchiveMetadataRecord(
                _snapshotId,
                "device-1",
                ModbusRuntimeRole.Client,
                1,
                _capturedAtUtc,
                0,
                2,
                400,
                2,
                "hash",
                ArchiveResolution.HighResolution,
                1);
            return Task.FromResult(ArchiveOperationResult<ArchivePage<RawModbusSnapshotArchiveMetadataRecord>>.Success(
                new ArchivePage<RawModbusSnapshotArchiveMetadataRecord>([row], query.PageNumber, query.PageSize, 2, true)));
        }

        public Task<ArchiveOperationResult<ArchivePage<RawModbusSnapshotArchiveRecord>>> QueryRawSnapshotsAsync(
            ArchiveQuery query,
            CancellationToken cancellationToken = default)
        {
            RawSnapshotQueryCount++;
            return Task.FromResult(SuccessPage<RawModbusSnapshotArchiveRecord>(query));
        }

        public Task<ArchiveOperationResult<RawModbusSnapshotArchiveRecord>> GetRawSnapshotAsync(
            Guid id,
            DateTimeOffset capturedAtUtc,
            CancellationToken cancellationToken = default)
        {
            DetailsQueryCount++;
            return Task.FromResult(ArchiveOperationResult<RawModbusSnapshotArchiveRecord>.Success(new RawModbusSnapshotArchiveRecord(
                id,
                "device-1",
                ModbusRuntimeRole.Client,
                1,
                capturedAtUtc,
                0,
                400,
                [true, false],
                [10, 20],
                "hash",
                ArchiveResolution.HighResolution,
                1)));
        }

        public Task<ArchiveOperationResult<ArchivePage<ModbusStatusArchiveRecord>>> QueryModbusStatusesAsync(
            ArchiveQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult(SuccessPage<ModbusStatusArchiveRecord>(query));

        public Task<ArchiveOperationResult<ArchivePage<ArchiveRuntimeEventRecord>>> QueryRuntimeEventsAsync(
            ArchiveQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult(SuccessPage<ArchiveRuntimeEventRecord>(query));

        public Task<ArchiveOperationResult<ArchivePage<EquipmentCommandAuditRecord>>> QueryEquipmentCommandsAsync(
            ArchiveQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult(SuccessPage<EquipmentCommandAuditRecord>(query));

        public Task<ArchiveOperationResult<ArchivePage<PhysicalModbusWriteAuditRecord>>> QueryPhysicalWritesAsync(
            ArchiveQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult(SuccessPage<PhysicalModbusWriteAuditRecord>(query));

        public Task<ArchiveOperationResult<ArchivePage<SecurityAuditRecord>>> QuerySecurityAuditAsync(
            ArchiveQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult(SuccessPage<SecurityAuditRecord>(query));

        private static ArchiveOperationResult<ArchivePage<T>> SuccessPage<T>(ArchiveQuery query)
            => ArchiveOperationResult<ArchivePage<T>>.Success(new ArchivePage<T>([], query.PageNumber, query.PageSize, 0, false));
    }

    private sealed class RecordingArchiveMaintenanceService : IArchiveMaintenanceService
    {
        public int ExportCount { get; private set; }

        public Task<ArchiveOperationResult<ArchiveRetentionResult>> ApplyRetentionAsync(
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ArchiveOperationResult<ArchiveRetentionResult>.Success(new ArchiveRetentionResult(nowUtc, [])));

        public Task<ArchiveOperationResult<ArchiveExportResult>> ExportAsync(
            ArchiveExportRequest request,
            CancellationToken cancellationToken = default,
            IProgress<ArchiveExportProgress>? progress = null)
        {
            ExportCount++;
            progress?.Report(new ArchiveExportProgress(ArchiveExportPhase.Completed, 0, "Done."));
            return Task.FromResult(ArchiveOperationResult<ArchiveExportResult>.Success(new ArchiveExportResult(
                Path.Combine(request.ExportDirectory ?? string.Empty, "export.zip"),
                DateTimeOffset.UtcNow,
                0,
                0,
                0,
                0,
                new Dictionary<string, string>())));
        }

        public Task<ArchiveOperationResult<ArchiveBackupResult>> CreateBackupAsync(
            string destinationDirectory,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ArchiveOperationResult<ArchiveBackupResult>.Success(new ArchiveBackupResult(
                Path.Combine(destinationDirectory, "backup.zip"),
                DateTimeOffset.UtcNow,
                [],
                new Dictionary<string, string>())));
    }

    private sealed class RecordingArchiveFilePicker : IArchiveFilePicker
    {
        public string? ExportDirectory { get; init; } = "exports";
        public string? BackupDirectory { get; init; } = "backups";

        public Task<string?> PickExportDirectoryAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ExportDirectory);

        public Task<string?> PickBackupDirectoryAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(BackupDirectory);
    }

    private sealed class RecordingArchiveHealthService : IArchiveHealthService
    {
        public ArchiveHealth Current { get; } = ArchiveHealth.Stopped;

        public IObservable<ArchiveHealth> Observe()
            => new EmptyHealthObservable(Current);
    }

    private sealed class EmptyHealthObservable(ArchiveHealth value) : IObservable<ArchiveHealth>
    {
        public IDisposable Subscribe(IObserver<ArchiveHealth> observer)
        {
            observer.OnNext(value);
            return new EmptyDisposable();
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
