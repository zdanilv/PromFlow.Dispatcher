using System.Collections.ObjectModel;
using Configurator.Application.Services.Archiving;
using Configurator.Desktop;
using ReactiveUI;

namespace Configurator.Desktop.Workspace.Archive;

public sealed class CommandAuditViewModel : ViewModelBase
{
    private readonly IArchiveQueryService _queryService;
    private ArchiveCommandAuditKind _kind;

    public CommandAuditViewModel(IArchiveQueryService queryService)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
    }

    public ArchiveCommandAuditKind Kind
    {
        get => _kind;
        set
        {
            this.RaiseAndSetIfChanged(ref _kind, value);
            this.RaisePropertyChanged(nameof(ShowEquipmentCommands));
            this.RaisePropertyChanged(nameof(ShowPhysicalWrites));
        }
    }

    public bool ShowEquipmentCommands => Kind == ArchiveCommandAuditKind.EquipmentCommands;

    public bool ShowPhysicalWrites => Kind == ArchiveCommandAuditKind.PhysicalWrites;

    public ObservableCollection<ArchiveCommandAuditRowViewModel> EquipmentCommands { get; } = [];

    public ObservableCollection<ArchivePhysicalWriteRowViewModel> PhysicalWrites { get; } = [];

    public async Task<(bool Succeeded, long TotalCount, bool HasMore, string? Error)> LoadAsync(
        ArchiveQuery query,
        CancellationToken cancellationToken)
    {
        if (Kind == ArchiveCommandAuditKind.PhysicalWrites)
        {
            var outcome = await _queryService.QueryPhysicalWritesAsync(query, cancellationToken).ConfigureAwait(true);
            if (!outcome.Succeeded || outcome.Value is null)
            {
                return (false, 0, false, FormatFailure(outcome.ErrorCode, outcome.ErrorMessage));
            }

            PhysicalWrites.Clear();
            foreach (var record in outcome.Value.Items)
            {
                PhysicalWrites.Add(new ArchivePhysicalWriteRowViewModel(record));
            }

            EquipmentCommands.Clear();
            return (true, outcome.Value.TotalCount, outcome.Value.HasMore, null);
        }

        var commands = await _queryService.QueryEquipmentCommandsAsync(query, cancellationToken).ConfigureAwait(true);
        if (!commands.Succeeded || commands.Value is null)
        {
            return (false, 0, false, FormatFailure(commands.ErrorCode, commands.ErrorMessage));
        }

        EquipmentCommands.Clear();
        foreach (var record in commands.Value.Items)
        {
            EquipmentCommands.Add(new ArchiveCommandAuditRowViewModel(record));
        }

        PhysicalWrites.Clear();
        return (true, commands.Value.TotalCount, commands.Value.HasMore, null);
    }

    private static string FormatFailure(string? code, string? message)
        => string.IsNullOrWhiteSpace(code) ? message ?? "Archive query failed." : $"{code}: {message}";
}
