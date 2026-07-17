namespace Configurator.Desktop.Workspace.ModbusProfile;

public interface IModbusTcpProfileFilePicker
{
    Task<string?> PickImportPathAsync(CancellationToken cancellationToken = default);

    Task<string?> PickExportPathAsync(CancellationToken cancellationToken = default);
}
