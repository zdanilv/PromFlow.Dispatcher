using System.Diagnostics;

namespace Configurator.Desktop.Dialogs.HelpDialog;

public sealed class ExternalLinkLauncher : IExternalLinkLauncher
{
    public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        return Task.CompletedTask;
    }
}
