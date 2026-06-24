using System.Reactive;
using Configurator.Application.Services.Archiving;
using Configurator.Desktop;
using ReactiveUI;

namespace Configurator.Desktop.Workspace.Archive;

public sealed class ArchiveMaintenanceViewModel : ViewModelBase, IDisposable
{
    private readonly IArchiveMaintenanceService _maintenanceService;
    private readonly IArchiveFilePicker _filePicker;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private string? _errorMessage;
    private string? _statusMessage;
    private bool _isBusy;

    public ArchiveMaintenanceViewModel(
        IArchiveMaintenanceService maintenanceService,
        IArchiveFilePicker filePicker)
    {
        _maintenanceService = maintenanceService ?? throw new ArgumentNullException(nameof(maintenanceService));
        _filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));

        ApplyRetentionCommand = ReactiveCommand.CreateFromTask(() =>
            ApplyRetentionAsync(_lifetimeCancellation.Token));
        CreateBackupCommand = ReactiveCommand.CreateFromTask(() =>
            CreateBackupAsync(_lifetimeCancellation.Token));
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _errorMessage, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public ReactiveCommand<Unit, Unit> ApplyRetentionCommand { get; }

    public ReactiveCommand<Unit, Unit> CreateBackupCommand { get; }

    public void Dispose()
    {
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
    }

    private async Task ApplyRetentionAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            var outcome = await _maintenanceService.ApplyRetentionAsync(DateTimeOffset.UtcNow, cancellationToken)
                .ConfigureAwait(true);
            if (!outcome.Succeeded || outcome.Value is null)
            {
                SetError(FormatFailure(outcome.ErrorCode, outcome.ErrorMessage));
                return;
            }

            ErrorMessage = null;
            StatusMessage = $"Retention applied. Deleted rows: {outcome.Value.DeletedHighResolutionSnapshotRows + outcome.Value.DeletedLongTermSnapshotRows + outcome.Value.DeletedRuntimeEventRows + outcome.Value.DeletedCommandRows + outcome.Value.DeletedPhysicalWriteRows + outcome.Value.DeletedSecurityAuditRows}.";
        }).ConfigureAwait(true);
    }

    private async Task CreateBackupAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            var directory = await _filePicker.PickBackupDirectoryAsync(cancellationToken).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            var outcome = await _maintenanceService.CreateBackupAsync(directory, cancellationToken)
                .ConfigureAwait(true);
            if (!outcome.Succeeded || outcome.Value is null)
            {
                SetError(FormatFailure(outcome.ErrorCode, outcome.ErrorMessage));
                return;
            }

            ErrorMessage = null;
            StatusMessage = $"Backup created: {outcome.Value.BackupPath}";
        }).ConfigureAwait(true);
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetError(string message)
    {
        ErrorMessage = message;
        StatusMessage = null;
    }

    private static string FormatFailure(string? code, string? message)
        => string.IsNullOrWhiteSpace(code) ? message ?? "Archive operation failed." : $"{code}: {message}";
}
