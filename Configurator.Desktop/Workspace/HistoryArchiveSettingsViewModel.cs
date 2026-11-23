using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Threading;
using Configurator.Application.Services;
using Configurator.Application.Services.History;
using Microsoft.Extensions.Options;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Configurator.Desktop.Workspace;

public sealed partial class HistoryArchiveSettingsViewModel : ViewModelBase, IDisposable
{
    private readonly IAppConfigService _config;
    private readonly IHistoryArchiveService _archive;
    private readonly bool _isAdmin;
    private readonly IDisposable _previewSubscription;
    private int _statusPosted;
    private volatile bool _disposed;

    [Reactive] private bool _enabled;
    [Reactive] private Choice _interval;
    [Reactive] private string _directory;
    [Reactive] private string _fileNameTemplate;
    [Reactive] private bool _archiveOnExit;
    [Reactive] private Choice _retention;
    [Reactive] private string _localDatabaseRetentionDays;
    [Reactive] private string _minimumFreeSpaceMb;
    [Reactive] private bool _isBusy;
    [Reactive] private string? _message;
    [Reactive] private string? _validationError;
    [Reactive] private HistoryArchiveStatus _status;

    public HistoryArchiveSettingsViewModel(
        IAppConfigService config,
        IHistoryArchiveService archive,
        IOptions<ApplicationOptions> applicationOptions)
    {
        _config = config;
        _archive = archive;
        _isAdmin = applicationOptions.Value.IsAdminMode;
        var options = archive.CurrentOptions;
        _enabled = options.Enabled;
        _directory = options.Directory;
        _fileNameTemplate = options.FileNameTemplate;
        _archiveOnExit = options.ArchiveOnExit;
        _localDatabaseRetentionDays = options.LocalDatabaseRetentionDays.ToString(CultureInfo.InvariantCulture);
        _minimumFreeSpaceMb = options.MinimumFreeSpaceMb.ToString(CultureInfo.InvariantCulture);
        Intervals = IncludeCurrent([new(15, "15 мин"), new(60, "1 ч"), new(360, "6 ч"), new(1440, "24 ч"), new(10080, "7 дней")],
            new(options.ArchiveIntervalMinutes, $"{options.ArchiveIntervalMinutes} мин"));
        Retentions = IncludeCurrent([new(0, "Не удалять"), new(30, "30 дней"), new(90, "90 дней"), new(180, "180 дней"), new(365, "365 дней")],
            new(options.RetentionDays, $"{options.RetentionDays} дней"));
        _interval = Intervals.Single(choice => choice.Value == options.ArchiveIntervalMinutes);
        _retention = Retentions.Single(choice => choice.Value == options.RetentionDays);
        _status = archive.CurrentStatus;

        var canEdit = this.WhenAnyValue(x => x.IsBusy, busy => _isAdmin && !busy);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, canEdit);
        ArchiveNowCommand = ReactiveCommand.CreateFromTask(ArchiveNowAsync, canEdit);
        _previewSubscription = this.WhenAnyValue(x => x.FileNameTemplate)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(FileNameExample)));
        if (_isAdmin)
        {
            _archive.StatusChanged += OnStatusChanged;
            Status = _archive.CurrentStatus;
        }
    }

    public IReadOnlyList<Choice> Intervals { get; }
    public IReadOnlyList<Choice> Retentions { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> ArchiveNowCommand { get; }

    public string FileNameExample => (FileNameTemplate ?? string.Empty)
        .Replace("{date}", DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{seq}", "000001", StringComparison.Ordinal);

    public string StateText => Status.State switch
    {
        HistoryArchiveState.Starting => "Запуск хранилища",
        HistoryArchiveState.Running => "Работает",
        HistoryArchiveState.Disabled => "Выключена плановая архивация",
        HistoryArchiveState.Archiving => "Идёт архивация",
        HistoryArchiveState.Warning => "Предупреждение",
        HistoryArchiveState.Error => "Ошибка",
        HistoryArchiveState.StorageInUse => "Хранилище занято",
        _ => "Остановлено"
    };

    public string LastArchiveText => Status.LastSuccessfulArchiveUtc is { } date
        ? $"{FormatTime(date)} · событий: {Status.LastArchiveEventCount:N0} · байт: {Status.LastArchiveSizeBytes:N0}"
        : "—";

    public string NextArchiveText => FormatTime(Status.NextArchiveDueUtc);

    internal async Task SelectFolderAsync(Func<Task<string?>> pickFolder)
    {
        if (!_isAdmin || IsBusy)
            return;

        IsBusy = true;
        try
        {
            if (await pickFolder() is { } path)
            {
                Directory = path;
                ValidationError = null;
            }
        }
        catch (Exception ex)
        {
            ValidationError = $"Не удалось выбрать папку. {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private async Task SaveAsync()
    {
        if (!_isAdmin || IsBusy)
            return;

        ValidationError = null;
        Message = null;
        if (Interval is null || Retention is null)
        {
            ValidationError = "Выберите частоту архивации и срок хранения ZIP.";
            return;
        }
        if (!int.TryParse(LocalDatabaseRetentionDays, NumberStyles.None, CultureInfo.InvariantCulture, out var databaseDays))
        {
            ValidationError = "Укажите срок хранения записей в БД целым числом суток.";
            return;
        }
        if (!int.TryParse(MinimumFreeSpaceMb, NumberStyles.None, CultureInfo.InvariantCulture, out var freeSpace))
        {
            ValidationError = "Укажите порог свободного места целым числом мегабайт.";
            return;
        }

        var draft = new HistoryArchiveOptions
        {
            Enabled = Enabled,
            ArchiveIntervalMinutes = Interval.Value,
            Directory = Directory?.Trim() ?? string.Empty,
            FileNameTemplate = FileNameTemplate ?? string.Empty,
            ArchiveOnExit = ArchiveOnExit,
            RetentionDays = Retention.Value,
            LocalDatabaseRetentionDays = databaseDays,
            MinimumFreeSpaceMb = freeSpace
        };
        IsBusy = true;
        var saved = false;
        try
        {
            await Task.Run(async () =>
            {
                await _archive.ValidateOptionsAsync(draft);
                await _config.SaveSectionAsync(HistoryArchiveOptions.SectionName, draft);
                saved = true;
                await _archive.ApplyOptionsAsync(draft);
            });
            Message = "Настройки архивирования сохранены и применены.";
        }
        catch (Exception ex)
        {
            ValidationError = saved
                ? $"Настройки сохранены. Применить их сейчас не удалось. {ex.Message}"
                : $"Не удалось сохранить настройки архивирования. {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private async Task ArchiveNowAsync()
    {
        if (!_isAdmin || IsBusy)
            return;

        IsBusy = true;
        Message = null;
        try
        {
            var result = await Task.Run(() => _archive.ArchiveNowAsync());
            Message = result.Outcome switch
            {
                ArchiveOutcome.Success => $"Создан архив {result.Path}",
                ArchiveOutcome.NoNewEvents => "Новых событий для архивации нет.",
                ArchiveOutcome.AlreadyRunning => "Архивация уже выполняется.",
                _ => result.Message ?? "Хранилище истории недоступно."
            };
        }
        catch (Exception ex) { Message = $"Не удалось создать архив. {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private void OnStatusChanged(object? sender, HistoryArchiveStatus args)
    {
        if (_disposed || Interlocked.Exchange(ref _statusPosted, 1) != 0)
            return;

        void Refresh()
        {
            Interlocked.Exchange(ref _statusPosted, 0);
            if (_disposed)
                return;
            Status = _archive.CurrentStatus;
            this.RaisePropertyChanged(nameof(StateText));
            this.RaisePropertyChanged(nameof(LastArchiveText));
            this.RaisePropertyChanged(nameof(NextArchiveText));
        }

        if (global::Avalonia.Application.Current is null)
            Refresh();
        else
            Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Background);
    }

    public void Dispose()
    {
        _disposed = true;
        _archive.StatusChanged -= OnStatusChanged;
        _previewSubscription.Dispose();
        SaveCommand.Dispose();
        ArchiveNowCommand.Dispose();
    }

    private static string FormatTime(DateTimeOffset? date) => date?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "—";

    private static IReadOnlyList<Choice> IncludeCurrent(Choice[] presets, Choice current) =>
        presets.Any(choice => choice.Value == current.Value) ? presets : [.. presets, current];

    public sealed record Choice(int Value, string Label)
    {
        public override string ToString() => Label;
    }
}
