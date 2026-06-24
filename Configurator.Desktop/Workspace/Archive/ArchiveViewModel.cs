using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive;
using Avalonia.Threading;
using Configurator.Application.Services.Archiving;
using Configurator.Desktop;
using Microsoft.Extensions.Options;
using ReactiveUI;

namespace Configurator.Desktop.Workspace.Archive;

public sealed class ArchiveViewModel : ViewModelBase, IDisposable
{
    private readonly IArchiveQueryService _queryService;
    private readonly IArchiveMaintenanceService _maintenanceService;
    private readonly IArchiveFilePicker _filePicker;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly IDisposable _healthSubscription;
    private CancellationTokenSource? _activeQueryCancellation;
    private ArchiveSection _selectedSection = ArchiveSection.Snapshots;
    private ArchiveSnapshotRowViewModel? _selectedSnapshot;
    private bool _isBusy;
    private bool _isExporting;
    private int _pageNumber = 1;
    private long _totalCount;
    private bool _hasMore;
    private string? _errorMessage;
    private string? _statusMessage;
    private string _healthState = ArchiveHealthState.Stopped.ToString();
    private string _healthMessage = ArchiveHealth.Stopped.Message;
    private bool _initialized;
    private bool _disposed;
    private int _exportProgressGeneration;

    public ArchiveViewModel(
        IArchiveQueryService queryService,
        IArchiveHealthService healthService,
        IArchiveMaintenanceService maintenanceService,
        IArchiveFilePicker filePicker,
        IOptions<ArchiveOptions> options)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        ArgumentNullException.ThrowIfNull(healthService);
        _maintenanceService = maintenanceService ?? throw new ArgumentNullException(nameof(maintenanceService));
        _filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));
        ArgumentNullException.ThrowIfNull(options);

        var archiveOptions = options.Value.Clone();
        Filter = new ArchiveFilterViewModel(archiveOptions.QueryMaxPageSize);
        CommandAudit = new CommandAuditViewModel(queryService);
        RuntimeEvents = new RuntimeEventsViewModel(queryService);
        SecurityAudit = new SecurityAuditViewModel(queryService);
        Maintenance = new ArchiveMaintenanceViewModel(maintenanceService, filePicker);

        InitializeCommand = ReactiveCommand.CreateFromTask(InitializeAsync);
        RefreshCommand = ReactiveCommand.CreateFromTask(() => LoadPageAsync(1));
        NextPageCommand = ReactiveCommand.CreateFromTask(() => LoadPageAsync(PageNumber + 1));
        PreviousPageCommand = ReactiveCommand.CreateFromTask(() => LoadPageAsync(Math.Max(1, PageNumber - 1)));
        CancelQueryCommand = ReactiveCommand.Create(CancelQuery);
        LoadSnapshotDetailsCommand = ReactiveCommand.CreateFromTask(LoadSnapshotDetailsAsync);
        ExportCommand = ReactiveCommand.CreateFromTask(ExportAsync);

        ApplyHealth(healthService.Current);
        _healthSubscription = healthService.Observe().Subscribe(new HealthObserver(this));
    }

    public ArchiveSection[] Sections { get; } = Enum.GetValues<ArchiveSection>();

    public ArchiveCommandAuditKind[] CommandKinds { get; } = Enum.GetValues<ArchiveCommandAuditKind>();

    public ArchiveSnapshotArea[] SnapshotAreas { get; } = Enum.GetValues<ArchiveSnapshotArea>();

    public ArchiveValueDisplayMode[] ValueDisplayModes { get; } = Enum.GetValues<ArchiveValueDisplayMode>();

    public ArchiveFilterViewModel Filter { get; }

    public ArchiveSnapshotDetailsViewModel SnapshotDetails { get; } = new();

    public CommandAuditViewModel CommandAudit { get; }

    public RuntimeEventsViewModel RuntimeEvents { get; }

    public SecurityAuditViewModel SecurityAudit { get; }

    public ArchiveMaintenanceViewModel Maintenance { get; }

    public ObservableCollection<ArchiveSnapshotRowViewModel> Snapshots { get; } = [];

    public ArchiveSection SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (_selectedSection != value)
            {
                this.RaiseAndSetIfChanged(ref _selectedSection, value);
                RaiseSectionVisibilityChanged();
            }
        }
    }

    public ArchiveSnapshotRowViewModel? SelectedSnapshot
    {
        get => _selectedSnapshot;
        set => this.RaiseAndSetIfChanged(ref _selectedSnapshot, value);
    }

    public bool ShowSnapshots => SelectedSection == ArchiveSection.Snapshots;

    public bool ShowCommands => SelectedSection == ArchiveSection.Commands;

    public bool ShowEvents => SelectedSection == ArchiveSection.Events;

    public bool ShowSecurityAudit => SelectedSection == ArchiveSection.SecurityAudit;

    public bool ShowMaintenance => SelectedSection == ArchiveSection.Maintenance;

    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public bool IsExporting
    {
        get => _isExporting;
        private set => this.RaiseAndSetIfChanged(ref _isExporting, value);
    }

    public int PageNumber
    {
        get => _pageNumber;
        private set
        {
            this.RaiseAndSetIfChanged(ref _pageNumber, value);
            this.RaisePropertyChanged(nameof(CanPrevious));
        }
    }

    public long TotalCount
    {
        get => _totalCount;
        private set => this.RaiseAndSetIfChanged(ref _totalCount, value);
    }

    public bool HasMore
    {
        get => _hasMore;
        private set => this.RaiseAndSetIfChanged(ref _hasMore, value);
    }

    public bool CanPrevious => PageNumber > 1;

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

    public string HealthState
    {
        get => _healthState;
        private set => this.RaiseAndSetIfChanged(ref _healthState, value);
    }

    public string HealthMessage
    {
        get => _healthMessage;
        private set => this.RaiseAndSetIfChanged(ref _healthMessage, value);
    }

    public ReactiveCommand<Unit, Unit> InitializeCommand { get; }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    public ReactiveCommand<Unit, Unit> NextPageCommand { get; }

    public ReactiveCommand<Unit, Unit> PreviousPageCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelQueryCommand { get; }

    public ReactiveCommand<Unit, Unit> LoadSnapshotDetailsCommand { get; }

    public ReactiveCommand<Unit, Unit> ExportCommand { get; }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await LoadPageAsync(1).ConfigureAwait(true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCancellation.Cancel();
        _activeQueryCancellation?.Cancel();
        _activeQueryCancellation?.Dispose();
        _lifetimeCancellation.Dispose();
        _healthSubscription.Dispose();
        Maintenance.Dispose();
    }

    private async Task LoadPageAsync(int pageNumber)
    {
        if (SelectedSection == ArchiveSection.Maintenance)
        {
            ResetPageState();
            StatusMessage = "Archive maintenance actions are available.";
            return;
        }

        var queryCancellation = ReplaceQueryCancellation();
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = "Loading archive page.";

        try
        {
            var result = await LoadSelectedSectionAsync(pageNumber, queryCancellation.Token).ConfigureAwait(true);
            if (!result.Succeeded)
            {
                SetError(result.Error ?? "Archive query failed.");
                return;
            }

            PageNumber = pageNumber;
            TotalCount = result.TotalCount;
            HasMore = result.HasMore;
            StatusMessage = $"Loaded page {PageNumber.ToString(CultureInfo.InvariantCulture)} of archive records.";
        }
        catch (OperationCanceledException) when (queryCancellation.IsCancellationRequested)
        {
            StatusMessage = "Archive query canceled.";
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or ArgumentException)
        {
            SetError(exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_activeQueryCancellation, queryCancellation))
            {
                _activeQueryCancellation = null;
            }

            queryCancellation.Dispose();
            IsBusy = false;
        }
    }

    private async Task<(bool Succeeded, long TotalCount, bool HasMore, string? Error)> LoadSelectedSectionAsync(
        int pageNumber,
        CancellationToken cancellationToken)
    {
        switch (SelectedSection)
        {
            case ArchiveSection.Snapshots:
                return await LoadSnapshotsAsync(pageNumber, cancellationToken).ConfigureAwait(true);
            case ArchiveSection.Commands:
                return await CommandAudit
                    .LoadAsync(Filter.CreateQuery(pageNumber, CommandRecordKind()), cancellationToken)
                    .ConfigureAwait(true);
            case ArchiveSection.Events:
                return await RuntimeEvents
                    .LoadAsync(Filter.CreateQuery(pageNumber), cancellationToken)
                    .ConfigureAwait(true);
            case ArchiveSection.SecurityAudit:
                return await SecurityAudit
                    .LoadAsync(Filter.CreateQuery(pageNumber, ArchiveRecordKind.SecurityAudit), cancellationToken)
                    .ConfigureAwait(true);
            default:
                return (true, 0, false, null);
        }
    }

    private async Task<(bool Succeeded, long TotalCount, bool HasMore, string? Error)> LoadSnapshotsAsync(
        int pageNumber,
        CancellationToken cancellationToken)
    {
        var query = Filter.CreateQuery(pageNumber, ArchiveRecordKind.RawModbusSnapshot);
        var outcome = await _queryService.QueryRawSnapshotMetadataAsync(query, cancellationToken).ConfigureAwait(true);
        if (!outcome.Succeeded || outcome.Value is null)
        {
            return (false, 0, false, FormatFailure(outcome.ErrorCode, outcome.ErrorMessage));
        }

        Snapshots.Clear();
        foreach (var record in outcome.Value.Items)
        {
            Snapshots.Add(new ArchiveSnapshotRowViewModel(record));
        }

        SelectedSnapshot = null;
        SnapshotDetails.Clear();
        return (true, outcome.Value.TotalCount, outcome.Value.HasMore, null);
    }

    private async Task LoadSnapshotDetailsAsync()
    {
        if (SelectedSnapshot is null)
        {
            SetError("Select a snapshot first.");
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var outcome = await _queryService
                .GetRawSnapshotAsync(
                    SelectedSnapshot.Id,
                    SelectedSnapshot.Record.CapturedAtUtc,
                    _lifetimeCancellation.Token)
                .ConfigureAwait(true);
            if (!outcome.Succeeded || outcome.Value is null)
            {
                SetError(FormatFailure(outcome.ErrorCode, outcome.ErrorMessage));
                return;
            }

            SnapshotDetails.Load(outcome.Value);
            StatusMessage = "Snapshot details loaded.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportAsync()
    {
        var progressGeneration = Interlocked.Increment(ref _exportProgressGeneration);
        IsExporting = true;
        ErrorMessage = null;
        try
        {
            var query = Filter.CreateBoundedExportQuery(ExportRecordKind());
            var directory = await _filePicker.PickExportDirectoryAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            var progress = new Progress<ArchiveExportProgress>(progress =>
                UpdateExportProgress(progress, progressGeneration));
            var outcome = await _maintenanceService
                .ExportAsync(new ArchiveExportRequest(query, directory), _lifetimeCancellation.Token, progress)
                .ConfigureAwait(true);
            if (!outcome.Succeeded || outcome.Value is null)
            {
                CompleteExportProgress(progressGeneration);
                SetError(FormatFailure(outcome.ErrorCode, outcome.ErrorMessage));
                return;
            }

            CompleteExportProgress(progressGeneration);
            StatusMessage = $"Archive export created: {outcome.Value.ExportPath}";
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or ArgumentException)
        {
            CompleteExportProgress(progressGeneration);
            SetError(exception.Message);
        }
        finally
        {
            CompleteExportProgress(progressGeneration);
            IsExporting = false;
        }
    }

    private void CancelQuery()
    {
        _activeQueryCancellation?.Cancel();
    }

    private CancellationTokenSource ReplaceQueryCancellation()
    {
        _activeQueryCancellation?.Cancel();
        _activeQueryCancellation?.Dispose();
        _activeQueryCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        return _activeQueryCancellation;
    }

    private ArchiveRecordKind? CommandRecordKind()
        => CommandAudit.Kind == ArchiveCommandAuditKind.PhysicalWrites
            ? ArchiveRecordKind.PhysicalModbusWriteAudit
            : ArchiveRecordKind.EquipmentCommandAudit;

    private ArchiveRecordKind? ExportRecordKind()
        => SelectedSection switch
        {
            ArchiveSection.Snapshots => ArchiveRecordKind.RawModbusSnapshot,
            ArchiveSection.Commands => CommandRecordKind(),
            ArchiveSection.SecurityAudit => ArchiveRecordKind.SecurityAudit,
            _ => null
        };

    private void ResetPageState()
    {
        PageNumber = 1;
        TotalCount = 0;
        HasMore = false;
    }

    private void UpdateExportProgress(ArchiveExportProgress progress, int generation)
    {
        if (Volatile.Read(ref _exportProgressGeneration) != generation)
        {
            return;
        }

        StatusMessage = $"{progress.Phase}: {progress.Message}";
    }

    private void CompleteExportProgress(int generation)
    {
        Interlocked.CompareExchange(ref _exportProgressGeneration, generation + 1, generation);
    }

    private void ApplyHealth(ArchiveHealth health)
    {
        HealthState = health.State.ToString();
        HealthMessage = health.Message;
    }

    private void SetError(string message)
    {
        ErrorMessage = message;
        StatusMessage = null;
    }

    private void RaiseSectionVisibilityChanged()
    {
        this.RaisePropertyChanged(nameof(ShowSnapshots));
        this.RaisePropertyChanged(nameof(ShowCommands));
        this.RaisePropertyChanged(nameof(ShowEvents));
        this.RaisePropertyChanged(nameof(ShowSecurityAudit));
        this.RaisePropertyChanged(nameof(ShowMaintenance));
    }

    private static string FormatFailure(string? code, string? message)
        => string.IsNullOrWhiteSpace(code) ? message ?? "Archive operation failed." : $"{code}: {message}";

    private sealed class HealthObserver(ArchiveViewModel owner) : IObserver<ArchiveHealth>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(ArchiveHealth value)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                owner.ApplyHealth(value);
                return;
            }

            Dispatcher.UIThread.Post(() => owner.ApplyHealth(value));
        }
    }
}
