using Configurator.Application.Services.Modbus.Runtime;

namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Bounded archive query contract used by later persistence and UI stages.
/// </summary>
public sealed record ArchiveQuery
{
    public ArchiveQuery(
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? deviceId = null,
        ModbusRuntimeRole? role = null,
        ArchiveRecordKind? recordKind = null,
        string? signalId = null,
        string? userId = null,
        string? result = null,
        int pageNumber = 1,
        int pageSize = 100,
        ArchiveSortDirection sortDirection = ArchiveSortDirection.Descending)
    {
        ArchiveContractGuards.Positive(pageNumber, nameof(pageNumber));
        ArchiveContractGuards.Positive(pageSize, nameof(pageSize));
        ArchiveContractGuards.EnumDefined(sortDirection, nameof(sortDirection));

        if (role is ModbusRuntimeRole concreteRole)
        {
            ArchiveContractGuards.EnumDefined(concreteRole, nameof(role));
        }

        if (recordKind is ArchiveRecordKind concreteKind)
        {
            ArchiveContractGuards.EnumDefined(concreteKind, nameof(recordKind));
        }

        FromUtc = ArchiveContractGuards.Utc(fromUtc);
        ToUtc = ArchiveContractGuards.Utc(toUtc);
        if (FromUtc > ToUtc)
        {
            throw new ArgumentException("Archive query start must be earlier than query end.", nameof(fromUtc));
        }

        DeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim();
        Role = role;
        RecordKind = recordKind;
        SignalId = string.IsNullOrWhiteSpace(signalId) ? null : signalId.Trim();
        UserId = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
        Result = string.IsNullOrWhiteSpace(result) ? null : result.Trim();
        PageNumber = pageNumber;
        PageSize = pageSize;
        SortDirection = sortDirection;
    }

    public DateTimeOffset? FromUtc { get; }

    public DateTimeOffset? ToUtc { get; }

    public string? DeviceId { get; }

    public ModbusRuntimeRole? Role { get; }

    public ArchiveRecordKind? RecordKind { get; }

    public string? SignalId { get; }

    public string? UserId { get; }

    public string? Result { get; }

    public int PageNumber { get; }

    public int PageSize { get; }

    public ArchiveSortDirection SortDirection { get; }
}
