namespace Configurator.Desktop.Workspace.Licensing;

public interface ILicenseFilePicker
{
    Task<string?> PickLicensePathAsync(CancellationToken cancellationToken = default);

    Task<string?> PickRequestExportPathAsync(CancellationToken cancellationToken = default);
}
