namespace Configurator.Application.Services.History;

public sealed record HistoryEvent(
    Guid EventUid,
    DateTimeOffset TimestampUtc,
    DateTimeOffset RecordedUtc,
    string EventType,
    string Name,
    string Roles,
    string Objects,
    string? ModbusArea,
    int? Address,
    int? BitIndex,
    string Value,
    string Source,
    Guid SessionId)
{
    public long Id { get; init; }
}

public interface IHistoryEventSink
{
    /// <summary>Offers an event to the writer without waiting for disk I/O.</summary>
    bool TryWrite(HistoryEvent historyEvent);
}

public interface IHistoryEventStore
{
    Task<int> AppendBatchAsync(IReadOnlyList<HistoryEvent> events, CancellationToken ct = default);

    IAsyncEnumerable<HistoryEvent> ReadRangeAsync(long afterId, long throughId, CancellationToken ct = default);
}
