using Configurator.Application.Services.Archiving;

namespace Configurator.Desktop.Workspace.Archive;

public sealed class ArchiveRuntimeEventRowViewModel
{
    public ArchiveRuntimeEventRowViewModel(ArchiveRuntimeEventRecord record)
    {
        Id = record.Id;
        OccurredAtUtc = record.OccurredAtUtc.ToString("u");
        DeviceId = record.DeviceId ?? string.Empty;
        EventType = record.EventType;
        Severity = record.Severity;
        Message = record.Message;
        DetailsJson = record.DetailsJson ?? string.Empty;
    }

    public Guid Id { get; }
    public string OccurredAtUtc { get; }
    public string DeviceId { get; }
    public string EventType { get; }
    public int Severity { get; }
    public string Message { get; }
    public string DetailsJson { get; }
}
