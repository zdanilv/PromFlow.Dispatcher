namespace Configurator.Application.Services.History;

public interface IHistoryArchiveService
{
    HistoryArchiveStatus CurrentStatus { get; }
    HistoryArchiveOptions CurrentOptions { get; }
    event EventHandler<HistoryArchiveStatus>? StatusChanged;

    Task StartAsync(CancellationToken ct = default);
    Task<ArchiveResult> ArchiveNowAsync(CancellationToken ct = default);
    Task ValidateOptionsAsync(HistoryArchiveOptions options, CancellationToken ct = default);
    Task ApplyOptionsAsync(HistoryArchiveOptions options, CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}

public enum ArchiveOutcome
{
    Success,
    NoNewEvents,
    AlreadyRunning,
    Unavailable,
    Error
}

public sealed record ArchiveResult(ArchiveOutcome Outcome, string? Path = null, string? Message = null);
