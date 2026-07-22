namespace Configurator.Desktop.Dialogs.HelpDialog;

/// <summary>
/// Opens a permitted external contact URI through the operating system's default handler.
/// </summary>
public interface IExternalLinkLauncher
{
    Task OpenAsync(Uri uri, CancellationToken cancellationToken = default);
}
