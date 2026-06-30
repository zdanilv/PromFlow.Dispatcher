using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.SignalMapping;
using ReactiveUI;

namespace Configurator.Desktop.Workspace.RouteMap.Services;

public sealed class RouteMapSessionJournal
{
    private readonly Dictionary<string, object?> _lastSignalValues = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<AlarmNotificationItem> Notifications { get; } = [];
    public ObservableCollection<RouteMapSessionHistoryItem> History { get; } = [];

    public void RecordSignalSent(
        SignalWriteRequest request,
        RouteMapDefinition definition,
        ModbusOptions options,
        DateTimeOffset timestamp)
    {
        var metadata = CreateMetadata(request.SignalId, definition, options.DataMap);
        History.Add(new RouteMapSessionHistoryItem(
            timestamp,
            "Отправлено",
            request.SignalId,
            metadata.Roles,
            metadata.Objects,
            metadata.Address,
            metadata.Bit,
            FormatValue(request.Value)));
    }

    public void RecordSignalSnapshot(
        IReadOnlyDictionary<string, SignalValue> signals,
        RouteMapDefinition definition,
        ModbusOptions options)
    {
        var inventory = RouteMapSignalInventory.Build(definition)
            .ToDictionary(item => item.SignalId, StringComparer.OrdinalIgnoreCase);

        foreach (var point in options.DataMap.Where(point => point.IsReadable))
        {
            if (IsInternalRuntimeSignal(point.Name)
                || !signals.TryGetValue(point.Name, out var signal)
                || !signal.IsQualityGood
                || signal.IsStale)
            {
                continue;
            }

            if (_lastSignalValues.TryGetValue(point.Name, out var previous)
                && ValuesEqual(previous, signal.Value))
            {
                continue;
            }

            _lastSignalValues[point.Name] = signal.Value;
            inventory.TryGetValue(point.Name, out var item);
            History.Add(new RouteMapSessionHistoryItem(
                signal.Timestamp,
                "Получено",
                point.Name,
                item?.Roles ?? string.Empty,
                item?.Objects ?? string.Empty,
                FormatAddress(point.Area, point.Address),
                FormatBit(point.BitIndex),
                FormatValue(signal.Value)));
        }
    }

    public void RecordAlarmActivated(ModbusAlarmOptions alarm, DateTimeOffset timestamp)
    {
        History.Add(CreateAlarmHistoryItem(timestamp, "Тревога", alarm, FormatValue(true), useAcknowledgementAddress: false));
    }

    public void ShowAlarmNotification(
        ModbusAlarmOptions alarm,
        DateTimeOffset createdAt,
        bool markUnread)
    {
        var item = Notifications.FirstOrDefault(
            candidate => string.Equals(candidate.Id, alarm.Id, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            Notifications.Insert(0, new AlarmNotificationItem(alarm, createdAt, isUnread: markUnread, isActive: true));
            return;
        }

        item.Apply(alarm, createdAt, markUnread, isActive: true);
        MoveNotificationToTop(item);
    }

    public void RecordAlarmCleared(ModbusAlarmOptions alarm, DateTimeOffset timestamp)
    {
        if (FindNotification(alarm.Id) is { } item)
        {
            item.IsActive = false;
        }

        History.Add(CreateAlarmHistoryItem(timestamp, "Снято", alarm, FormatValue(false), useAcknowledgementAddress: false));
    }

    public void RecordAlarmAcknowledged(ModbusAlarmOptions alarm, DateTimeOffset timestamp)
    {
        MarkAlarmRead(alarm.Id);
        History.Add(CreateAlarmHistoryItem(timestamp, "OK", alarm, "OK", useAcknowledgementAddress: true));
    }

    public void MarkAlarmRead(string alarmId)
    {
        if (FindNotification(alarmId) is { } item)
        {
            item.IsUnread = false;
        }
    }

    public bool IsAlarmActive(string alarmId) => FindNotification(alarmId)?.IsActive is true;

    public bool TryDismissAlarm(string alarmId)
    {
        var item = FindNotification(alarmId);
        if (item is null)
        {
            return true;
        }

        if (item.IsActive)
        {
            return false;
        }

        Notifications.Remove(item);
        return true;
    }

    public int ClearDismissibleNotifications()
    {
        var removed = 0;
        foreach (var item in Notifications.Where(item => !item.IsActive).ToArray())
        {
            Notifications.Remove(item);
            removed++;
        }

        return removed;
    }

    public IReadOnlyList<RouteMapSessionHistoryItem> SnapshotHistory() => History.ToArray();

    private AlarmNotificationItem? FindNotification(string alarmId) =>
        Notifications.FirstOrDefault(item => string.Equals(item.Id, alarmId, StringComparison.OrdinalIgnoreCase));

    private void MoveNotificationToTop(AlarmNotificationItem item)
    {
        var index = Notifications.IndexOf(item);
        if (index <= 0)
        {
            return;
        }

        Notifications.Move(index, 0);
    }

    private static RouteMapSessionHistoryItem CreateAlarmHistoryItem(
        DateTimeOffset timestamp,
        string eventText,
        ModbusAlarmOptions alarm,
        string valueText,
        bool useAcknowledgementAddress)
    {
        var address = useAcknowledgementAddress ? alarm.Acknowledgement : alarm.Alarm;
        return new RouteMapSessionHistoryItem(
            timestamp,
            eventText,
            alarm.Id,
            AlarmKindTitle(alarm.Kind),
            alarm.Message,
            FormatAddress(address.Area, address.Address),
            FormatBit(address.BitIndex),
            valueText);
    }

    private static SignalMetadata CreateMetadata(
        string signalId,
        RouteMapDefinition definition,
        IReadOnlyList<ModbusDataPointOptions> dataMap)
    {
        var inventory = RouteMapSignalInventory.Build(definition)
            .FirstOrDefault(item => string.Equals(item.SignalId, signalId, StringComparison.OrdinalIgnoreCase));
        var point = dataMap.FirstOrDefault(item => string.Equals(item.Name, signalId, StringComparison.OrdinalIgnoreCase));

        return new SignalMetadata(
            inventory?.Roles ?? string.Empty,
            inventory?.Objects ?? string.Empty,
            point is null ? string.Empty : FormatAddress(point.Area, point.Address),
            point is null ? string.Empty : FormatBit(point.BitIndex));
    }

    private static bool IsInternalRuntimeSignal(string signalId) =>
        string.Equals(signalId, RouteMapSystemSignalIds.ConnectionStatus, StringComparison.OrdinalIgnoreCase)
        || string.Equals(signalId, RouteMapSystemSignalIds.ConnectionConnected, StringComparison.OrdinalIgnoreCase);

    private static bool ValuesEqual(object? left, object? right) => Equals(left, right);

    private static string FormatAddress(ModbusDataArea area, int address) =>
        address.ToString(CultureInfo.InvariantCulture);

    private static string FormatBit(int? bitIndex) =>
        bitIndex.HasValue ? bitIndex.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private static string FormatValue(object? value) =>
        value switch
        {
            null => string.Empty,
            bool flag => flag ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

    private static string AlarmKindTitle(ModbusAlarmKind kind) => kind switch
    {
        ModbusAlarmKind.Fault => "Авария",
        ModbusAlarmKind.Confirmation => "Повторное подтверждение",
        ModbusAlarmKind.Message => "Сообщение",
        _ => "Сообщение"
    };

    private sealed record SignalMetadata(string Roles, string Objects, string Address, string Bit);
}

public sealed class AlarmNotificationItem : ReactiveObject
{
    private ModbusAlarmKind _kind;
    private string _message = string.Empty;
    private DateTimeOffset _createdAt;
    private bool _isUnread;
    private bool _isActive;
    private ModbusBitAddressOptions _acknowledgement = new();
    private int _acknowledgementPulseDurationMs;

    public AlarmNotificationItem(
        ModbusAlarmOptions alarm,
        DateTimeOffset createdAt,
        bool isUnread,
        bool isActive)
    {
        Id = alarm.Id;
        Apply(alarm, createdAt, markUnread: isUnread, isActive);
    }

    public string Id { get; }

    public ModbusAlarmKind Kind
    {
        get => _kind;
        private set
        {
            this.RaiseAndSetIfChanged(ref _kind, value);
            RaiseVisualProperties();
        }
    }

    public string Message
    {
        get => _message;
        private set => this.RaiseAndSetIfChanged(ref _message, value);
    }

    public DateTimeOffset CreatedAt
    {
        get => _createdAt;
        private set
        {
            this.RaiseAndSetIfChanged(ref _createdAt, value);
            this.RaisePropertyChanged(nameof(CreatedAtText));
        }
    }

    public bool IsUnread
    {
        get => _isUnread;
        set
        {
            this.RaiseAndSetIfChanged(ref _isUnread, value);
            this.RaisePropertyChanged(nameof(IsRead));
        }
    }

    public bool IsRead => !IsUnread;

    public bool IsActive
    {
        get => _isActive;
        set => this.RaiseAndSetIfChanged(ref _isActive, value);
    }

    public ModbusBitAddressOptions Acknowledgement => _acknowledgement.Clone();
    public int AcknowledgementPulseDurationMs => _acknowledgementPulseDurationMs;
    public string CreatedAtText => CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture);
    public string Title => AlarmKindTitle(Kind);
    public string BadgeText => Kind switch
    {
        ModbusAlarmKind.Fault => "!",
        ModbusAlarmKind.Confirmation => "?",
        ModbusAlarmKind.Message => "i",
        _ => "i"
    };

    public IBrush HeaderBackground => Kind switch
    {
        ModbusAlarmKind.Fault => new SolidColorBrush(Color.Parse("#B42318")),
        ModbusAlarmKind.Confirmation => new SolidColorBrush(Color.Parse("#9A6700")),
        ModbusAlarmKind.Message => new SolidColorBrush(Color.Parse("#295B8D")),
        _ => new SolidColorBrush(Color.Parse("#295B8D"))
    };

    public IBrush BadgeBackground => Kind switch
    {
        ModbusAlarmKind.Fault => new SolidColorBrush(Color.Parse("#FEE4E2")),
        ModbusAlarmKind.Confirmation => new SolidColorBrush(Color.Parse("#FFF4CC")),
        ModbusAlarmKind.Message => new SolidColorBrush(Color.Parse("#E8F2FF")),
        _ => new SolidColorBrush(Color.Parse("#E8F2FF"))
    };

    public IBrush BadgeForeground => Kind switch
    {
        ModbusAlarmKind.Fault => new SolidColorBrush(Color.Parse("#B42318")),
        ModbusAlarmKind.Confirmation => new SolidColorBrush(Color.Parse("#8A5A00")),
        ModbusAlarmKind.Message => new SolidColorBrush(Color.Parse("#295B8D")),
        _ => new SolidColorBrush(Color.Parse("#295B8D"))
    };

    public void Apply(
        ModbusAlarmOptions alarm,
        DateTimeOffset createdAt,
        bool markUnread,
        bool isActive)
    {
        Kind = alarm.Kind;
        Message = alarm.Message;
        _acknowledgement = alarm.Acknowledgement.Clone();
        _acknowledgementPulseDurationMs = alarm.AcknowledgementPulseDurationMs;
        IsActive = isActive;

        if (markUnread)
        {
            CreatedAt = createdAt;
            IsUnread = true;
        }
    }

    private void RaiseVisualProperties()
    {
        this.RaisePropertyChanged(nameof(Title));
        this.RaisePropertyChanged(nameof(BadgeText));
        this.RaisePropertyChanged(nameof(HeaderBackground));
        this.RaisePropertyChanged(nameof(BadgeBackground));
        this.RaisePropertyChanged(nameof(BadgeForeground));
    }

    private static string AlarmKindTitle(ModbusAlarmKind kind) => kind switch
    {
        ModbusAlarmKind.Fault => "Авария",
        ModbusAlarmKind.Confirmation => "Повторное подтверждение",
        ModbusAlarmKind.Message => "Сообщение",
        _ => "Сообщение"
    };
}

public sealed record RouteMapSessionHistoryItem(
    DateTimeOffset Timestamp,
    string EventText,
    string Name,
    string Roles,
    string Objects,
    string Address,
    string Bit,
    string Value)
{
    public string TimestampText => Timestamp.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture);
    public string DirectionGlyph => EventText switch
    {
        "Отправлено" or "OK" => "↑",
        "Получено" or "Тревога" or "Снято" => "↓",
        _ => string.Empty
    };
    public string DirectionText => EventText;
}

public interface ISessionJournalExporter
{
    Task ExportAsync(RouteMapSessionJournal journal, CancellationToken cancellationToken = default);
}

public sealed class NoopSessionJournalExporter : ISessionJournalExporter
{
    public Task ExportAsync(RouteMapSessionJournal journal, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
