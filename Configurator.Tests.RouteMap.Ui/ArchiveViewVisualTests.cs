using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Desktop.Workspace.Archive;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Tests.RouteMap.Ui;

public sealed class ArchiveViewVisualTests
{
    [AvaloniaFact]
    public void ArchiveView_RendersSectionsFiltersAndActions()
    {
        using var viewModel = new ArchiveViewModel(
            new FakeArchiveQueryService(),
            new FakeArchiveHealthService(),
            new FakeMaintenanceService(),
            new FakeFilePicker(),
            Options.Create(new ArchiveOptions { QueryMaxPageSize = 100 }));
        var view = new ArchiveView { DataContext = viewModel };
        var window = new Window { Width = 1200, Height = 760, Content = view };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
        Assert.Contains("Archive", texts);
        Assert.Contains("Health:", texts);
        Assert.Contains("Archive running.", texts);
        Assert.Contains(view.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Refresh"));
        Assert.Contains(view.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Export"));
        Assert.NotEmpty(view.GetVisualDescendants().OfType<ListBox>());
        window.Close();
    }

    private sealed class FakeArchiveQueryService : IArchiveQueryService
    {
        public Task<ArchiveOperationResult<ArchivePage<RawModbusSnapshotArchiveMetadataRecord>>> QueryRawSnapshotMetadataAsync(
            ArchiveQuery query,
            CancellationToken cancellationToken = default)
        {
            var row = new RawModbusSnapshotArchiveMetadataRecord(
                Guid.NewGuid(),
                "device-1",
                ModbusRuntimeRole.Client,
                1,
                DateTimeOffset.UtcNow,
                0,
                2,
                400,
                2,
                "hash",
                ArchiveResolution.HighResolution,
                1);
            return Task.FromResult(ArchiveOperationResult<ArchivePage<RawModbusSnapshotArchiveMetadataRecord>>.Success(
                new ArchivePage<RawModbusSnapshotArchiveMetadataRecord>([row], query.PageNumber, query.PageSize, 1, false)));
        }

        public Task<ArchiveOperationResult<ArchivePage<RawModbusSnapshotArchiveRecord>>> QueryRawSnapshotsAsync(
            ArchiveQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult(SuccessPage<RawModbusSnapshotArchiveRecord>(query));

        public Task<ArchiveOperationResult<RawModbusSnapshotArchiveRecord>> GetRawSnapshotAsync(
            Guid id,
            DateTimeOffset capturedAtUtc,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ArchiveOperationResult<RawModbusSnapshotArchiveRecord>.Failure("NotUsed", "Not used."));

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

    private sealed class FakeArchiveHealthService : IArchiveHealthService
    {
        public ArchiveHealth Current { get; } = new(
            ArchiveHealthState.Healthy,
            DateTimeOffset.UtcNow,
            "Archive running.",
            0,
            0);

        public IObservable<ArchiveHealth> Observe()
            => new HealthObservable(Current);
    }

    private sealed class HealthObservable(ArchiveHealth value) : IObservable<ArchiveHealth>
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

    private sealed class FakeMaintenanceService : IArchiveMaintenanceService
    {
        public Task<ArchiveOperationResult<ArchiveRetentionResult>> ApplyRetentionAsync(
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ArchiveOperationResult<ArchiveRetentionResult>.Success(new ArchiveRetentionResult(nowUtc, [])));

        public Task<ArchiveOperationResult<ArchiveExportResult>> ExportAsync(
            ArchiveExportRequest request,
            CancellationToken cancellationToken = default,
            IProgress<ArchiveExportProgress>? progress = null)
            => Task.FromResult(ArchiveOperationResult<ArchiveExportResult>.Success(new ArchiveExportResult(
                "export.zip",
                DateTimeOffset.UtcNow,
                0,
                0,
                0,
                0,
                new Dictionary<string, string>())));

        public Task<ArchiveOperationResult<ArchiveBackupResult>> CreateBackupAsync(
            string destinationDirectory,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ArchiveOperationResult<ArchiveBackupResult>.Success(new ArchiveBackupResult(
                "backup.zip",
                DateTimeOffset.UtcNow,
                [],
                new Dictionary<string, string>())));
    }

    private sealed class FakeFilePicker : IArchiveFilePicker
    {
        public Task<string?> PickExportDirectoryAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<string?> PickBackupDirectoryAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }
}
