namespace Configurator.Desktop.Workspace;

public sealed class WorkspaceAccessUnavailableViewModel
{
    public string Title { get; } = "No licensed workspace features are available.";

    public string Detail { get; } = "Contact an administrator to install or replace the product license.";
}
