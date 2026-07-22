using ReactiveUI;
using System.Reactive;

namespace Configurator.Desktop.Dialogs.HelpDialog;

public sealed class HelpContactViewModel
{
    public HelpContactViewModel(
        string label,
        string value,
        Uri? uri,
        IExternalLinkLauncher externalLinkLauncher)
    {
        Label = label;
        Value = value;
        Uri = uri;
        OpenLinkCommand = ReactiveCommand.CreateFromTask(
            cancellationToken => uri is null
                ? Task.CompletedTask
                : externalLinkLauncher.OpenAsync(uri, cancellationToken));
    }

    public string Label { get; }

    public string Value { get; }

    public Uri? Uri { get; }

    public bool IsLink => Uri is not null;

    public bool IsPlainText => !IsLink;

    public ReactiveCommand<Unit, Unit> OpenLinkCommand { get; }
}
