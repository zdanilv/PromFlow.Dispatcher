namespace Configurator.Application.Services.Licensing;

public static class LicenseFeature
{
    public const string RouteMap = "RouteMap";
    public const string RemoteControl = "RemoteControl";
    public const string Archive = "Archive";
    public const string ArchiveExport = "ArchiveExport";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        RouteMap,
        RemoteControl,
        Archive,
        ArchiveExport
    };
}
