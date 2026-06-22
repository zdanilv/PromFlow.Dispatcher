namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Single bounded archive query page.
/// </summary>
public sealed record ArchivePage<T>
{
    public ArchivePage(
        IReadOnlyList<T> items,
        int pageNumber,
        int pageSize,
        long totalCount,
        bool hasMore)
    {
        ArchiveContractGuards.Positive(pageNumber, nameof(pageNumber));
        ArchiveContractGuards.Positive(pageSize, nameof(pageSize));
        ArchiveContractGuards.NonNegative(totalCount, nameof(totalCount));

        Items = ArchiveContractGuards.ReadOnlyCopy(items, nameof(items));
        PageNumber = pageNumber;
        PageSize = pageSize;
        TotalCount = totalCount;
        HasMore = hasMore;
    }

    public IReadOnlyList<T> Items { get; }

    public int PageNumber { get; }

    public int PageSize { get; }

    public long TotalCount { get; }

    public bool HasMore { get; }
}
