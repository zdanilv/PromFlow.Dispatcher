using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Threading;
using Configurator.Application.Services.Dialogs;
using Configurator.Application.Services.History;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.SignalMapping;
using ReactiveUI;

namespace Configurator.Desktop.Workspace.RouteMap.Services;

public sealed class RouteMapSessionJournal
{
    public const int HistoryCapacity = 1000;

    private static readonly Guid SessionId = Guid.NewGuid();
    private readonly object _historySync = new();
    private readonly object _displaySync = new();
    private readonly Dictionary<string, object?> _lastSignalValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<RouteMapSessionHistoryItem> _pendingHistory = new(HistoryCapacity);
    private readonly IHistoryEventSink? _sink;
    private bool _historyUpdateScheduled;

    public RouteMapSessionJournal() { }

    public RouteMapSessionJournal(IHistoryEventSink sink) => _sink = sink;

    public ObservableCollection<AlarmNotificationItem> Notifications { get; } = [];
    public ObservableCollection<RouteMapSessionHistoryItem> History { get; } = [];

    public void RecordSignalSent(
        SignalWriteRequest request,
        RouteMapDefinition definition,
        ModbusOptions options,
        DateTimeOffset timestamp) => RecordSignalSent(request, definition, options, timestamp, null);

    internal void RecordSignalSent(
        SignalWriteRequest request,
        RouteMapDefinition definition,
        ModbusOptions options,
        DateTimeOffset timestamp,
        RouteMapSignalSource? source)
    {
        var metadata = CreateMetadata(request.SignalId, definition, options.DataMap);
        AddHistory(
            timestamp,
            "Отправлено",
            request.SignalId,
            metadata.Roles,
            metadata.Objects,
            metadata.Area,
            metadata.Address,
            metadata.Bit,
            request.Value,
            source?.ToString() ?? "Unknown");
    }

    public void RecordSignalSnapshot(
        IReadOnlyDictionary<string, SignalValue> signals,
        RouteMapDefinition definition,
        ModbusOptions options) => RecordSignalSnapshot(signals, definition, options, null);

    internal void RecordSignalSnapshot(
        IReadOnlyDictionary<string, SignalValue> signals,
        RouteMapDefinition definition,
        ModbusOptions options,
        RouteMapSignalSource? source)
    {
        lock (_historySync)
        {
            RecordChangedSignals(signals, definition, options, source?.ToString() ?? "Unknown");
        }
    }

    internal void ResetSignalSnapshot()
    {
        lock (_historySync)
        {
            _lastSignalValues.Clear();
        }
    }

    private void RecordChangedSignals(
        IReadOnlyDictionary<string, SignalValue> signals,
        RouteMapDefinition definition,
        ModbusOptions options,
        string source)
    {
        Dictionary<string, RouteMapSignalInventoryItem>? inventory = null;
        foreach (var point in options.DataMap)
        {
            if (!point.IsReadable || IsInternalRuntimeSignal(point.Name)
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
            inventory ??= RouteMapSignalInventory.Build(definition)
                .ToDictionary(item => item.SignalId, StringComparer.OrdinalIgnoreCase);
            inventory.TryGetValue(point.Name, out var item);
            AddHistory(
                signal.Timestamp,
                "Получено",
                point.Name,
                item?.Roles ?? string.Empty,
                item?.Objects ?? string.Empty,
                point.Area,
                point.Address,
                point.BitIndex,
                signal.Value,
                source);
        }
    }

    public void RecordAlarmActivated(ModbusAlarmOptions alarm, DateTimeOffset timestamp)
    {
        AddAlarmHistory(timestamp, "Тревога", alarm, true, alarm.Alarm);
    }

    public void ShowAlarmNotification(
        ModbusAlarmOptions alarm,
        DateTimeOffset createdAt,
        bool markUnread,
        string? registerValueText = null)
        => ShowAlarmNotification(alarm, createdAt, markUnread, registerValueText, null);

    internal void ShowAlarmNotification(
        ModbusAlarmOptions alarm,
        DateTimeOffset createdAt,
        bool markUnread,
        string? registerValueText,
        AlarmAcknowledgementContext? context)
    {
        var item = Notifications.FirstOrDefault(
            candidate => string.Equals(candidate.Id, alarm.Id, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            var notification = new AlarmNotificationItem(
                alarm,
                createdAt,
                isUnread: markUnread,
                isActive: true,
                registerValueText: registerValueText);
            notification.AcknowledgementContext = context;
            Notifications.Insert(0, notification);
            return;
        }

        item.AcknowledgementContext = context;
        item.Apply(
            alarm,
            createdAt,
            markUnread,
            isActive: true,
            registerValueText: registerValueText);
        MoveNotificationToTop(item);
    }

    internal void InvalidateAlarmNotification(AlarmAcknowledgementContext context)
    {
        if (FindNotification(context.AlarmId) is { } item
            && ReferenceEquals(item.AcknowledgementContext, context))
        {
            item.IsActive = false;
        }
    }

    // Both dialog entry points finish here. No mutable notification/configuration is
    // read after awaiting the dialog, and no journal state is changed after the I/O.
    internal async Task<ModbusOperationResult?> CompleteAlarmAcknowledgementAsync(
        AlarmAcknowledgementContext? context,
        IModbusBitWriter writer,
        CancellationToken cancellationToken = default,
        IDialogService? dialogService = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context is null || !context.IsCurrent()
            || FindNotification(context.AlarmId) is not { IsActive: true } item
            || !ReferenceEquals(item.AcknowledgementContext, context))
        {
            return null;
        }

        var alarm = context.CopyAlarm();
        RecordAlarmAcknowledged(alarm, DateTimeOffset.Now);
        if (!alarm.AcknowledgementEnabled || !context.IsCurrent())
        {
            return null;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, context.CancellationToken);
        try
        {
            // Invalidation also cancels an ACK waiting in the writer's gate.
            linked.Token.ThrowIfCancellationRequested();
            var result = await writer.PulseAsync(alarm.Acknowledgement, alarm.AcknowledgementPulseDurationMs, linked.Token);
            if (result.ErrorCode == ModbusAlarmMapValidator.AcknowledgementDataMapConflict
                && dialogService is not null && context.IsCurrent())
            {
                await dialogService.ShowErrorAsync("Подтверждение тревоги",
                    $"Тревога «{alarm.Id}» подтверждена локально. {result.ErrorMessage}", ct: linked.Token);
            }

            return result;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested
                                                  && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public void RecordAlarmCleared(ModbusAlarmOptions alarm, DateTimeOffset timestamp)
    {
        UpdateNotification(() =>
        {
            if (FindNotification(alarm.Id) is { } item)
            {
                item.IsActive = false;
            }
        });

        AddAlarmHistory(timestamp, "Снято", alarm, false, alarm.Alarm);
    }

    public void RecordAlarmAcknowledged(ModbusAlarmOptions alarm, DateTimeOffset timestamp)
    {
        UpdateNotification(() => MarkAlarmRead(alarm.Id));
        AddAlarmHistory(timestamp, "OK", alarm, "OK", alarm.Acknowledgement);
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

    public IReadOnlyList<RouteMapSessionHistoryItem> SnapshotHistory()
    {
        lock (_displaySync)
        {
            return History.ToArray();
        }
    }

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

    private static void UpdateNotification(Action action)
    {
        if (global::Avalonia.Application.Current is not null && !Dispatcher.UIThread.CheckAccess())
            Dispatcher.UIThread.Post(action);
        else
            action();
    }

    private void AddAlarmHistory(
        DateTimeOffset timestamp,
        string eventText,
        ModbusAlarmOptions alarm,
        object value,
        ModbusBitAddressOptions address) => AddHistory(
            timestamp, eventText, alarm.Id, AlarmKindTitle(alarm.Kind), alarm.Message,
            address.Area, address.Address, address.BitIndex, value, "Modbus");

    private void AddHistory(
        DateTimeOffset timestamp,
        string eventType,
        string name,
        string roles,
        string objects,
        ModbusDataArea? area,
        int? address,
        int? bit,
        object? value,
        string source)
    {
        var item = new RouteMapSessionHistoryItem(timestamp, eventType, name, roles, objects,
            FormatNumber(address), FormatNumber(bit), FormatValue(value));
        lock (_historySync)
        {
            _sink?.TryWrite(new HistoryEvent(Guid.NewGuid(), timestamp.ToUniversalTime(), DateTimeOffset.UtcNow,
                eventType, name, roles, objects, area?.ToString(), address, bit, FormatStoredValue(value), source, SessionId));

            if (global::Avalonia.Application.Current is null ||
                (_sink is null && !_historyUpdateScheduled && Dispatcher.UIThread.CheckAccess()))
            {
                lock (_displaySync)
                    AppendDisplayItem(item);
                return;
            }

            if (_pendingHistory.Count == HistoryCapacity)
                _pendingHistory.Dequeue();
            _pendingHistory.Enqueue(item);
            if (!_historyUpdateScheduled)
            {
                _historyUpdateScheduled = true;
                Dispatcher.UIThread.Post(ApplyPendingHistory, DispatcherPriority.Background);
            }
        }
    }

    private void ApplyPendingHistory()
    {
        RouteMapSessionHistoryItem[] pending;
        lock (_historySync)
        {
            pending = _pendingHistory.ToArray();
            _pendingHistory.Clear();
            _historyUpdateScheduled = false;
        }

        lock (_displaySync)
        {
            foreach (var item in pending)
                AppendDisplayItem(item);
        }
    }

    private void AppendDisplayItem(RouteMapSessionHistoryItem item)
    {
        if (History.Count >= HistoryCapacity)
            History.RemoveAt(0);
        History.Add(item);
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
            point?.Area,
            point?.Address,
            point?.BitIndex);
    }

    private static bool IsInternalRuntimeSignal(string signalId) =>
        string.Equals(signalId, RouteMapSystemSignalIds.ConnectionStatus, StringComparison.OrdinalIgnoreCase)
        || string.Equals(signalId, RouteMapSystemSignalIds.ConnectionConnected, StringComparison.OrdinalIgnoreCase);

    private static bool ValuesEqual(object? left, object? right) => Equals(left, right);

    private static string FormatNumber(int? number) => number?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FormatValue(object? value) =>
        value switch
        {
            DateTime dateTime => dateTime.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture),
            _ => FormatStoredValue(value)
        };

    private static string FormatStoredValue(object? value) => value switch
        {
            null => string.Empty,
            bool flag => flag ? "true" : "false",
            DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
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

    private sealed record SignalMetadata(string Roles, string Objects, ModbusDataArea? Area, int? Address, int? Bit);
}

public sealed class AlarmNotificationItem : ReactiveObject
{
    private ModbusAlarmKind _kind;
    private string _message = string.Empty;
    private string? _registerValueText;
    private DateTimeOffset _createdAt;
    private bool _isUnread;
    private bool _isActive;
    private ModbusBitAddressOptions _acknowledgement = new();
    private int _acknowledgementPulseDurationMs;

    public AlarmNotificationItem(
        ModbusAlarmOptions alarm,
        DateTimeOffset createdAt,
        bool isUnread,
        bool isActive,
        string? registerValueText = null)
    {
        Id = alarm.Id;
        Apply(
            alarm,
            createdAt,
            markUnread: isUnread,
            isActive: isActive,
            registerValueText: registerValueText);
    }

    public string Id { get; }

    internal AlarmAcknowledgementContext? AcknowledgementContext { get; set; }

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

    public string? RegisterValueText
    {
        get => _registerValueText;
        private set
        {
            this.RaiseAndSetIfChanged(ref _registerValueText, value);
            this.RaisePropertyChanged(nameof(HasRegisterValueText));
        }
    }

    public bool HasRegisterValueText => !string.IsNullOrWhiteSpace(RegisterValueText);

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
        bool isActive,
        string? registerValueText = null)
    {
        Kind = alarm.Kind;
        Message = alarm.Message;
        RegisterValueText = registerValueText;
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

internal sealed class AlarmAcknowledgementContext(
    ModbusAlarmOptions alarm,
    long activationToken,
    long configurationGeneration,
    Func<bool> isCurrent,
    CancellationToken cancellationToken)
{
    private readonly ModbusAlarmOptions _alarm = alarm.Clone();

    public string AlarmId => _alarm.Id;
    public long ActivationToken { get; } = activationToken;
    public long ConfigurationGeneration { get; } = configurationGeneration;
    public CancellationToken CancellationToken { get; } = cancellationToken;
    public bool IsCurrent() => !CancellationToken.IsCancellationRequested && isCurrent();
    public ModbusAlarmOptions CopyAlarm() => _alarm.Clone();
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
