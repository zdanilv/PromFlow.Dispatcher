using ReactiveUI.Avalonia;

namespace Configurator.Desktop.Workspace.ModbusDemo;

/// <summary>
/// Представление Avalonia для высокоуровневого Modbus TCP демо-экрана.
/// </summary>
public partial class ModbusDemoView : ReactiveUserControl<ModbusDemoViewModel>
{
    public ModbusDemoView()
    {
        InitializeComponent();
    }
}
