using Avalonia.Platform.Storage;
using Configurator.Desktop.Main;

namespace Configurator.Desktop.Workspace.ModbusProfile;

public sealed class ModbusTcpProfileFilePicker(MainWindow mainWindow) : IModbusTcpProfileFilePicker
{
    private static readonly FilePickerFileType JsonFileType = new("JSON")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"]
    };

    public async Task<string?> PickImportPathAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = await mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Импорт профиля Modbus TCP",
            AllowMultiple = false,
            FileTypeFilter = [JsonFileType]
        });
        cancellationToken.ThrowIfCancellationRequested();
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> PickExportPathAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = await mainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Экспорт профиля Modbus TCP",
            SuggestedFileName = "modbus-tcp-profile.json",
            DefaultExtension = "json",
            FileTypeChoices = [JsonFileType]
        });
        cancellationToken.ThrowIfCancellationRequested();
        return file?.TryGetLocalPath();
    }
}
