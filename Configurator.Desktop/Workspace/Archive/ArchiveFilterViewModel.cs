using System.Globalization;
using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Desktop;
using ReactiveUI;

namespace Configurator.Desktop.Workspace.Archive;

public sealed class ArchiveFilterViewModel : ViewModelBase
{
    private readonly int _maxPageSize;
    private string _fromLocalText;
    private string _toLocalText;
    private string _deviceId = string.Empty;
    private string _eventType = string.Empty;
    private string _signalId = string.Empty;
    private string _userId = string.Empty;
    private string _outcome = string.Empty;
    private int _pageSize;
    private ArchiveSortDirection _sortDirection = ArchiveSortDirection.Descending;
    private ModbusRuntimeRole? _role;

    public ArchiveFilterViewModel(int maxPageSize)
    {
        _maxPageSize = Math.Max(1, maxPageSize);
        var now = DateTimeOffset.Now;
        _fromLocalText = FormatLocal(now.AddHours(-24));
        _toLocalText = FormatLocal(now);
        _pageSize = Math.Min(100, _maxPageSize);
    }

    public string FromLocalText
    {
        get => _fromLocalText;
        set => this.RaiseAndSetIfChanged(ref _fromLocalText, value);
    }

    public string ToLocalText
    {
        get => _toLocalText;
        set => this.RaiseAndSetIfChanged(ref _toLocalText, value);
    }

    public string DeviceId
    {
        get => _deviceId;
        set => this.RaiseAndSetIfChanged(ref _deviceId, value);
    }

    public ModbusRuntimeRole? Role
    {
        get => _role;
        set => this.RaiseAndSetIfChanged(ref _role, value);
    }

    public string EventType
    {
        get => _eventType;
        set => this.RaiseAndSetIfChanged(ref _eventType, value);
    }

    public string SignalId
    {
        get => _signalId;
        set => this.RaiseAndSetIfChanged(ref _signalId, value);
    }

    public string UserId
    {
        get => _userId;
        set => this.RaiseAndSetIfChanged(ref _userId, value);
    }

    public string Outcome
    {
        get => _outcome;
        set => this.RaiseAndSetIfChanged(ref _outcome, value);
    }

    public int PageSize
    {
        get => _pageSize;
        set => this.RaiseAndSetIfChanged(ref _pageSize, value);
    }

    public ArchiveSortDirection SortDirection
    {
        get => _sortDirection;
        set => this.RaiseAndSetIfChanged(ref _sortDirection, value);
    }

    public ArchiveQuery CreateQuery(int pageNumber, ArchiveRecordKind? recordKind = null)
    {
        var fromUtc = ParseLocalToUtc(FromLocalText, nameof(FromLocalText));
        var toUtc = ParseLocalToUtc(ToLocalText, nameof(ToLocalText));
        if (fromUtc is not null && toUtc is not null && fromUtc > toUtc)
        {
            throw new InvalidOperationException("Archive filter start must be earlier than end.");
        }

        return new ArchiveQuery(
            fromUtc,
            toUtc,
            DeviceId,
            Role,
            recordKind,
            EventType,
            SignalId,
            UserId,
            Outcome,
            pageNumber,
            Math.Clamp(PageSize, 1, _maxPageSize),
            SortDirection);
    }

    public ArchiveQuery CreateBoundedExportQuery(ArchiveRecordKind? recordKind = null)
    {
        var query = CreateQuery(pageNumber: 1, recordKind);
        if (query.FromUtc is null || query.ToUtc is null)
        {
            throw new InvalidOperationException("Archive export requires both start and end timestamps.");
        }

        return query;
    }

    private static DateTimeOffset? ParseLocalToUtc(string? value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!DateTime.TryParse(
            value.Trim(),
            CultureInfo.CurrentCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var parsed))
        {
            throw new FormatException($"{propertyName} is not a valid local date/time.");
        }

        var unspecified = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        var offset = TimeZoneInfo.Local.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset).ToUniversalTime();
    }

    private static string FormatLocal(DateTimeOffset value)
        => value.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
}
