namespace Configurator.Application.Services.Archiving;

public enum ArchiveExportPhase
{
    Preparing,
    Commands,
    PhysicalWrites,
    RuntimeEvents,
    Snapshots,
    Packaging,
    Completed
}

public sealed record ArchiveExportProgress(
    ArchiveExportPhase Phase,
    long ProcessedRecords,
    string Message);
