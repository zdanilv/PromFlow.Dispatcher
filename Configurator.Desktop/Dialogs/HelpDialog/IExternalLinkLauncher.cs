namespace Configurator.Desktop.Dialogs.HelpDialog;

/// <summary>
/// Opens an external web link through the operating system's default handler.
/// </summary>
public interface IExternalLinkLauncher
{
    Task OpenAsync(Uri uri, CancellationToken cancellationToken = default);
}
