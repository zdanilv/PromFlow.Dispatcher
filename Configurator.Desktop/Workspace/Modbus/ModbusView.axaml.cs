using Avalonia.Markup.Xaml;
using ReactiveUI.Avalonia;

namespace Configurator.Desktop.Workspace.Modbus;

/// <summary>
/// Представление Avalonia экрана управления Modbus клиентом и сервером.
/// </summary>
public partial class ModbusView : ReactiveUserControl<ModbusViewModel>
{
    /// <summary>
    /// Загружает XAML-разметку экрана.
    /// </summary>
    public ModbusView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
