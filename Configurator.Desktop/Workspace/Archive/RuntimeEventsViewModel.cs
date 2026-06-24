using System.Collections.ObjectModel;
using Configurator.Application.Services.Archiving;
using Configurator.Desktop;

namespace Configurator.Desktop.Workspace.Archive;

public sealed class RuntimeEventsViewModel : ViewModelBase
{
    private readonly IArchiveQueryService _queryService;

    public RuntimeEventsViewModel(IArchiveQueryService queryService)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
    }

    public ObservableCollection<ArchiveRuntimeEventRowViewModel> Events { get; } = [];

    public async Task<(bool Succeeded, long TotalCount, bool HasMore, string? Error)> LoadAsync(
        ArchiveQuery query,
        CancellationToken cancellationToken)
    {
        var outcome = await _queryService.QueryRuntimeEventsAsync(query, cancellationToken).ConfigureAwait(true);
        if (!outcome.Succeeded || outcome.Value is null)
        {
            return (false, 0, false, FormatFailure(outcome.ErrorCode, outcome.ErrorMessage));
        }

        Events.Clear();
        foreach (var record in outcome.Value.Items)
        {
            Events.Add(new ArchiveRuntimeEventRowViewModel(record));
        }

        return (true, outcome.Value.TotalCount, outcome.Value.HasMore, null);
    }

    private static string FormatFailure(string? code, string? message)
        => string.IsNullOrWhiteSpace(code) ? message ?? "Archive query failed." : $"{code}: {message}";
}
