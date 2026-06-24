namespace Configurator.Application.Services.Authorization;

public enum Permission
{
    ViewRouteMap = 0,
    IssueEquipmentCommands = 1,
    ViewArchive = 2,
    ExportArchive = 3,
    ViewSignalMapping = 4,
    EditSignalMapping = 5,
    ViewModbusDiagnostics = 6,
    ConfigureModbus = 7,
    ManageUsers = 8,
    InstallLicense = 9,
    ViewLicense = 10,
    ViewSecurityAudit = 11,
    RunArchiveMaintenance = 12
}
