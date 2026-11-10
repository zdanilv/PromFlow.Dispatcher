using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Configurator.Desktop.Workspace.RouteMap.Settings;

public partial class RouteMapSettingsDialog : UserControl
{
    public static readonly FuncValueConverter<string?, IBrush> StatusColorPreview = new(value =>
        value is { Length: 7 or 9 } && value[0] == '#' && Color.TryParse(value, out var color)
            ? new SolidColorBrush(color)
            : Brushes.Transparent);

    public RouteMapSettingsDialog()
    {
        InitializeComponent();
    }
}
