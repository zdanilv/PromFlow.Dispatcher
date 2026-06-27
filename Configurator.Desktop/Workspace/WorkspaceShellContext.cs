using System.Reactive;
using ReactiveUI;

namespace Configurator.Desktop.Workspace;

public sealed record WorkspaceShellContext(
    string CurrentUsername,
    ReactiveCommand<Unit, Unit> LogoutCommand);

public interface IWorkspaceShellContextConsumer
{
    void ApplyWorkspaceShellContext(WorkspaceShellContext context);
}
