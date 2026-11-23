namespace Configurator.Application.Services.History;

public sealed record HistoryArchiveOptions
{
    public const string SectionName = "HistoryArchive";
    public const string DefaultFileNameTemplate = "PromFlow_History_{date}_{seq}.zip";

    public bool Enabled { get; init; } = true;
    public int ArchiveIntervalMinutes { get; init; } = 1440;
    public string Directory { get; init; } = string.Empty;
    public string FileNameTemplate { get; init; } = DefaultFileNameTemplate;
    public bool ArchiveOnExit { get; init; } = true;
    public int RetentionDays { get; init; }
    public int LocalDatabaseRetentionDays { get; init; } = 30;
    public int MinimumFreeSpaceMb { get; init; } = 1024;
}
