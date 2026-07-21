namespace Configurator.Desktop.Workspace.RouteMap.Settings;

public interface IEquipmentParameterAutoDispatcher
{
    Task DispatchPendingAsync(CancellationToken cancellationToken = default);
}
