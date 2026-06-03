using Avalonia.Markup.Xaml;
using ReactiveUI.Avalonia;

namespace Configurator.Desktop.Workspace.OpcUa;

/// <summary>
/// Представление Avalonia экрана управления OPC UA клиентом и сервером.
/// </summary>
public partial class OpcUaView : ReactiveUserControl<OpcUaViewModel>
{
    /// <summary>
    /// Загружает XAML-разметку экрана.
    /// </summary>
    public OpcUaView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
