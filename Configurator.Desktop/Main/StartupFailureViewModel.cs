using ReactiveUI;

namespace Configurator.Desktop.Main;

public sealed class StartupFailureViewModel : ViewModelBase, IRoutableViewModel
{
    public StartupFailureViewModel(IScreen hostScreen, string message)
    {
        HostScreen = hostScreen ?? throw new ArgumentNullException(nameof(hostScreen));
        Message = string.IsNullOrWhiteSpace(message)
            ? "Application startup failed."
            : message;
    }

    public string UrlPathSegment => "startup-failure";

    public IScreen HostScreen { get; }

    public string Message { get; }
}
