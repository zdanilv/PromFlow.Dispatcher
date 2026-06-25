using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Configurator.Desktop.Runtime;

public interface IDesktopShutdownCoordinator
{
    bool IsCloseAllowed { get; }

    Task RequestShutdownAsync(
        Window? window,
        IClassicDesktopStyleApplicationLifetime? applicationLifetime,
        CancellationToken cancellationToken = default);
}
