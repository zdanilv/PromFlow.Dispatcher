using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Configurator.Desktop.Workspace;

public partial class HistoryArchiveSettingsView : UserControl
{
    public HistoryArchiveSettingsView() => InitializeComponent();

    private async void BrowseFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HistoryArchiveSettingsViewModel settings || TopLevel.GetTopLevel(this) is not { } owner)
            return;

        await settings.SelectFolderAsync(async () =>
        {
            var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Папка для архивов истории",
                AllowMultiple = false
            });
            return folders.FirstOrDefault()?.TryGetLocalPath();
        });
    }
}
