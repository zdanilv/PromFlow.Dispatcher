namespace Configurator.Application.Services.Archiving;

/// <summary>
/// Request for a bounded archive export.
/// </summary>
public sealed record ArchiveExportRequest
{
    public ArchiveExportRequest(
        ArchiveQuery query,
        string? exportDirectory = null,
        ArchiveExportFormat format = ArchiveExportFormat.ZipPackage)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArchiveContractGuards.EnumDefined(format, nameof(format));

        Query = query;
        ExportDirectory = string.IsNullOrWhiteSpace(exportDirectory) ? null : exportDirectory.Trim();
        Format = format;
    }

    public ArchiveQuery Query { get; }

    public string? ExportDirectory { get; }

    public ArchiveExportFormat Format { get; }
}
