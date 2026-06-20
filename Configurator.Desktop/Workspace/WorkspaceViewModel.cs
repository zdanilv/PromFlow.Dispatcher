using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Desktop.Workspace.ModbusDemo;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;
using Configurator.Desktop.Workspace.RouteMap.SignalMapping;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Configurator.Desktop.Workspace
{
    public partial class WorkspaceViewModel : ViewModelBase, IRoutableViewModel, IDisposable
    {
        private readonly CancellationTokenSource _lifetimeCancellation = new();
        private bool _disposed;
        public string Name { get; set; } = "Work Page";
        public string UrlPathSegment => "main";
        public IScreen HostScreen { get; }

        // Example data passed from the login screen: authorization token.
        public string AuthToken { get; }
        public ModbusDemoViewModel ModbusDemo { get; }
        public RouteMapDashboardViewModel RouteMapDashboard { get; }
        public RouteMapSignalMappingViewModel RouteMapSignalMapping { get; }

        public WorkspaceViewModel(
            IScreen hostScreen,
            IAuthApp authService,
            ModbusDemoViewModel modbusDemo,
            RouteMapDashboardViewModel routeMapDashboard,
            RouteMapSignalMappingViewModel routeMapSignalMapping,
            IModbusRuntimeService modbusRuntime,
            IModbusDemoOptionsProvider modbusOptions,
            ILogger<WorkspaceViewModel> logger)
        {
            HostScreen = hostScreen;
            AuthToken = authService.IsAuthenticated.ToString();
            ModbusDemo = modbusDemo;
            RouteMapDashboard = routeMapDashboard;
            RouteMapSignalMapping = routeMapSignalMapping;

            var options = modbusOptions.CurrentValue.Clone();
            if (options.AutostartOnWorkspaceOpen && options.StartupMode != ModbusRunMode.None)
            {
                _ = StartModbusAsync(modbusRuntime, options, logger, _lifetimeCancellation.Token);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _lifetimeCancellation.Cancel();
            _lifetimeCancellation.Dispose();
            RouteMapDashboard.Dispose();
            RouteMapSignalMapping.Dispose();
            ModbusDemo.Dispose();
        }

        private static async Task StartModbusAsync(
            IModbusRuntimeService runtime,
            ModbusOptions options,
            ILogger<WorkspaceViewModel> logger,
            CancellationToken cancellationToken)
        {
            try
            {
                await runtime.StartAsync(options.StartupMode, options, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Failed to autostart Modbus in {StartupMode} mode.",
                    options.StartupMode);
            }
        }
    }
}
