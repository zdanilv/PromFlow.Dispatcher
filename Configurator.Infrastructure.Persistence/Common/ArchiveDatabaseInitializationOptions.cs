namespace Configurator.Infrastructure.Persistence.Common;

public sealed class ArchiveDatabaseInitializationOptions
{
    public string DatabasePath { get; init; } = string.Empty;

    public string DeviceId { get; init; } = string.Empty;

    public string ApplicationVersion { get; init; } = string.Empty;

    public int ArchiveSchemaVersion { get; init; } = 1;

    public int BusyTimeoutMs { get; init; } = 5000;
}
