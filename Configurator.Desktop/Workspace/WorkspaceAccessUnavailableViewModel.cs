namespace Configurator.Desktop.Workspace;

public sealed class WorkspaceAccessUnavailableViewModel(
    string? title = null,
    string? detail = null)
{
    public string Title { get; } = string.IsNullOrWhiteSpace(title)
        ? "No licensed workspace features are available."
        : title;

    public string Detail { get; } = string.IsNullOrWhiteSpace(detail)
        ? "Contact an administrator to install or replace the product license."
        : detail;
}
