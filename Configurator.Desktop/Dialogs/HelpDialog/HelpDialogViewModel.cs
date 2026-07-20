using ReactiveUI;
using System.Reactive;

namespace Configurator.Desktop.Dialogs.HelpDialog;

public sealed class HelpDialogViewModel : ReactiveObject
{
    public const string Phone = "8 953 448 31 16";
    public const string Email = "example@example.com";
    public const string Website = "example.com";
    public static readonly Uri WebsiteUri = new("https://example.com");

    public HelpDialogViewModel(IExternalLinkLauncher externalLinkLauncher)
    {
        OpenWebsiteCommand = ReactiveCommand.CreateFromTask(
            cancellationToken => externalLinkLauncher.OpenAsync(WebsiteUri, cancellationToken));
        CloseCommand = ReactiveCommand.Create(() => true);
        Result = CloseCommand;
    }

    public ReactiveCommand<Unit, Unit> OpenWebsiteCommand { get; }
    public ReactiveCommand<Unit, bool> CloseCommand { get; }
    public IObservable<bool> Result { get; }
}
