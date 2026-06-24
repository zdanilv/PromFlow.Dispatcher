using Configurator.Application.Services.Archiving;
using Configurator.Desktop.Workspace.Archive;
using System.Reactive.Threading.Tasks;
using Xunit;

namespace Configurator.Tests.Unit.Archiving;

public sealed class ArchiveMaintenanceViewModelTests
{
    [Fact]
    public async Task ApplyRetentionCommand_MapsSuccessToStatus()
    {
        var service = new RecordingMaintenanceService();
        var viewModel = new ArchiveMaintenanceViewModel(service, new Picker());

        await viewModel.ApplyRetentionCommand.Execute().ToTask();

        Assert.Equal(1, service.RetentionCount);
        Assert.Contains("Retention applied", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task CreateBackupCommand_PickerCancellationDoesNotCallService()
    {
        var service = new RecordingMaintenanceService();
        var viewModel = new ArchiveMaintenanceViewModel(service, new Picker { BackupDirectory = null });

        await viewModel.CreateBackupCommand.Execute().ToTask();

        Assert.Equal(0, service.BackupCount);
    }

    private sealed class RecordingMaintenanceService : IArchiveMaintenanceService
    {
        public int RetentionCount { get; private set; }
        public int BackupCount { get; private set; }

        public Task<ArchiveOperationResult<ArchiveRetentionResult>> ApplyRetentionAsync(
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken = default)
        {
            RetentionCount++;
            return Task.FromResult(ArchiveOperationResult<ArchiveRetentionResult>.Success(new ArchiveRetentionResult(nowUtc, [])));
        }

        public Task<ArchiveOperationResult<ArchiveExportResult>> ExportAsync(
            ArchiveExportRequest request,
            CancellationToken cancellationToken = default,
            IProgress<ArchiveExportProgress>? progress = null)
            => Task.FromResult(ArchiveOperationResult<ArchiveExportResult>.Failure("NotUsed", "Not used."));

        public Task<ArchiveOperationResult<ArchiveBackupResult>> CreateBackupAsync(
            string destinationDirectory,
            CancellationToken cancellationToken = default)
        {
            BackupCount++;
            return Task.FromResult(ArchiveOperationResult<ArchiveBackupResult>.Success(new ArchiveBackupResult(
                Path.Combine(destinationDirectory, "backup.zip"),
                DateTimeOffset.UtcNow,
                [],
                new Dictionary<string, string>())));
        }
    }

    private sealed class Picker : IArchiveFilePicker
    {
        public string? BackupDirectory { get; init; } = "backups";

        public Task<string?> PickExportDirectoryAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<string?> PickBackupDirectoryAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(BackupDirectory);
    }
}
