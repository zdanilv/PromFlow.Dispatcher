using Configurator.Application.Services.Authorization;
using Configurator.Desktop.Workspace.Modbus;
using Configurator.Desktop.Workspace.ModbusDemo;
using Configurator.Desktop.Workspace.OpcUa;
using ReactiveUI;

namespace Configurator.Desktop.Workspace
{
    public partial class WorkspaceViewModel : ViewModelBase, IRoutableViewModel
    {
        public string Name { get; set; } = "Work Page";
        public string UrlPathSegment => "main";
        public IScreen HostScreen { get; }

        // Example data passed from the login screen: authorization token.
        public string AuthToken { get; }
        public ModbusViewModel Modbus { get; }
        public ModbusDemoViewModel ModbusDemo { get; }
        public OpcUaViewModel OpcUa { get; }

        public WorkspaceViewModel(
            IScreen hostScreen,
            IAuthApp authService,
            ModbusViewModel modbus,
            ModbusDemoViewModel modbusDemo,
            OpcUaViewModel opcUa)
        {
            HostScreen = hostScreen;
            AuthToken = authService.IsAuthenticated.ToString();
            Modbus = modbus;
            ModbusDemo = modbusDemo;
            OpcUa = opcUa;

            // Workspace data can be initialized here using the auth token.
        }
    }
}
