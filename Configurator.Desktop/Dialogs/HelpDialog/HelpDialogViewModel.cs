using Configurator.Application.Services;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using System.Reactive;

namespace Configurator.Desktop.Dialogs.HelpDialog;

public sealed class HelpDialogViewModel : ReactiveObject
{
    private static readonly HashSet<string> AllowedUriSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
        Uri.UriSchemeMailto,
        "tel"
    };

    public HelpDialogViewModel(
        IHelpOptionsProvider helpOptionsProvider,
        IExternalLinkLauncher externalLinkLauncher,
        ILogger<HelpDialogViewModel> logger)
    {
        Contacts = CreateContacts(helpOptionsProvider.GetCurrent(), externalLinkLauncher, logger);
        CloseCommand = ReactiveCommand.Create(() => true);
        Result = CloseCommand;
    }

    public IReadOnlyList<HelpContactViewModel> Contacts { get; }

    public bool HasContacts => Contacts.Count > 0;

    public bool HasNoContacts => !HasContacts;

    public ReactiveCommand<Unit, bool> CloseCommand { get; }

    public IObservable<bool> Result { get; }

    private static IReadOnlyList<HelpContactViewModel> CreateContacts(
        HelpOptions options,
        IExternalLinkLauncher externalLinkLauncher,
        ILogger logger)
    {
        var contacts = new List<HelpContactViewModel>();
        foreach (var configuredContact in options.Contacts)
        {
            var label = configuredContact.Label?.Trim();
            var value = configuredContact.Value?.Trim();
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value))
            {
                logger.LogWarning("Ignored help contact with an empty label or value.");
                continue;
            }

            if (contacts.Count == HelpOptions.MaximumContacts)
            {
                logger.LogWarning("Ignored help contacts above the {MaximumContacts} entry limit.", HelpOptions.MaximumContacts);
                break;
            }

            Uri? uri = null;
            if (!string.IsNullOrWhiteSpace(configuredContact.Uri))
            {
                if (Uri.TryCreate(configuredContact.Uri, UriKind.Absolute, out var parsedUri) &&
                    AllowedUriSchemes.Contains(parsedUri.Scheme))
                {
                    uri = parsedUri;
                }
                else
                {
                    logger.LogWarning("Ignored unsupported help contact URI for {Label}.", label);
                }
            }

            contacts.Add(new HelpContactViewModel(label, value, uri, externalLinkLauncher));
        }

        return contacts;
    }
}
