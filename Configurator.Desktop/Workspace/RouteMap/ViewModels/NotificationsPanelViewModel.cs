using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive;
using Configurator.Application.Services;
using Configurator.Application.Services.Dialogs;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Microsoft.Extensions.Options;
using ReactiveUI;

namespace Configurator.Desktop.Workspace.RouteMap.ViewModels;

public sealed class NotificationsPanelViewModel : ViewModelBase, IDisposable
{
    private readonly RouteMapSessionJournal _journal;
    private readonly IDialogService _dialogService;
    private readonly IModbusBitWriter _bitWriter;
    private bool _disposed;

    public NotificationsPanelViewModel(
        RouteMapSessionJournal journal,
        IDialogService dialogService,
        IModbusBitWriter bitWriter,
        IOptions<ApplicationOptions>? applicationOptions = null)
    {
        _journal = journal;
        _dialogService = dialogService;
        _bitWriter = bitWriter;
        IsHistoryVisible = applicationOptions?.Value.IsAdminMode ?? true;

        Notifications = journal.Notifications;
        History = journal.History;
        ShowNotificationCommand = ReactiveCommand.CreateFromTask<AlarmNotificationItem>(ShowNotificationAsync);
        DismissNotificationCommand = ReactiveCommand.Create<AlarmNotificationItem>(DismissNotification);
        ClearNotificationsCommand = ReactiveCommand.Create(ClearNotifications);

        Notifications.CollectionChanged += OnCollectionChanged;
        History.CollectionChanged += OnCollectionChanged;
    }

    public ObservableCollection<AlarmNotificationItem> Notifications { get; }
    public ObservableCollection<RouteMapSessionHistoryItem> History { get; }
    public bool HasNotifications => Notifications.Count > 0;
    public bool HasNoNotifications => !HasNotifications;
    public bool HasHistory => History.Count > 0;
    public bool HasNoHistory => !HasHistory;
    public bool IsHistoryVisible { get; }
    public ReactiveCommand<AlarmNotificationItem, Unit> ShowNotificationCommand { get; }
    public ReactiveCommand<AlarmNotificationItem, Unit> DismissNotificationCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearNotificationsCommand { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Notifications.CollectionChanged -= OnCollectionChanged;
        History.CollectionChanged -= OnCollectionChanged;
    }

    private async Task ShowNotificationAsync(AlarmNotificationItem item)
    {
        var confirmed = await _dialogService.ShowAlarmNotificationAsync(
            new AlarmNotificationContent(item.Kind, item.Message, item.RegisterValueText));
        if (!confirmed)
        {
            return;
        }

        var alarm = new ModbusAlarmOptions
        {
            Id = item.Id,
            Kind = item.Kind,
            Message = item.Message,
            Acknowledgement = item.Acknowledgement,
            AcknowledgementPulseDurationMs = item.AcknowledgementPulseDurationMs
        };

        _journal.RecordAlarmAcknowledged(alarm, DateTimeOffset.Now);
        if (_journal.IsAlarmActive(item.Id))
        {
            await _bitWriter.PulseAsync(item.Acknowledgement, item.AcknowledgementPulseDurationMs);
        }
    }

    private void DismissNotification(AlarmNotificationItem item) => _journal.TryDismissAlarm(item.Id);

    private void ClearNotifications() => _journal.ClearDismissibleNotifications();

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, Notifications))
        {
            this.RaisePropertyChanged(nameof(HasNotifications));
            this.RaisePropertyChanged(nameof(HasNoNotifications));
        }

        if (ReferenceEquals(sender, History))
        {
            this.RaisePropertyChanged(nameof(HasHistory));
            this.RaisePropertyChanged(nameof(HasNoHistory));
        }
    }
}
