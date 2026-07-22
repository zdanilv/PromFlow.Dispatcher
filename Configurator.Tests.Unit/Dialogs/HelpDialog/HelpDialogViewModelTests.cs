using System.Reactive.Linq;
using Configurator.Application.Services;
using Configurator.Desktop.Dialogs.HelpDialog;
using Microsoft.Extensions.Logging.Abstractions;
using ReactiveUI.Builder;
using Xunit;

namespace Configurator.Tests.Unit.Dialogs;

public sealed class HelpDialogViewModelTests
{
    static HelpDialogViewModelTests()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();
    }

    [Fact]
    public async Task Exposes_configured_contacts_and_opens_permitted_link()
    {
        var launcher = new RecordingExternalLinkLauncher();
        var viewModel = CreateViewModel(
            [
                new() { Label = "Телефон", Value = "8 953 448 31 16" },
                new() { Label = "Сайт", Value = "example.com", Uri = "https://example.com" }
            ],
            launcher);

        Assert.Collection(
            viewModel.Contacts,
            phone =>
            {
                Assert.Equal("Телефон", phone.Label);
                Assert.Equal("8 953 448 31 16", phone.Value);
                Assert.False(phone.IsLink);
            },
            website =>
            {
                Assert.Equal("Сайт", website.Label);
                Assert.True(website.IsLink);
            });

        await viewModel.Contacts[1].OpenLinkCommand.Execute().FirstAsync();

        Assert.Equal(new Uri("https://example.com"), launcher.OpenedUri);
    }

    [Fact]
    public void Keeps_only_first_ten_nonempty_contacts_and_renders_invalid_uri_as_text()
    {
        var contacts = Enumerable.Range(1, 12)
            .Select(index => new HelpContactOptions { Label = $"Контакт {index}", Value = $"Значение {index}" })
            .Prepend(new HelpContactOptions { Label = " ", Value = "Игнорируется" })
            .Append(new HelpContactOptions { Label = "Небезопасная ссылка", Value = "file", Uri = "file:///C:/temp" })
            .ToList();

        var viewModel = CreateViewModel(contacts, new RecordingExternalLinkLauncher());

        Assert.Equal(HelpOptions.MaximumContacts, viewModel.Contacts.Count);
        Assert.Equal("Контакт 1", viewModel.Contacts[0].Label);
        Assert.DoesNotContain(viewModel.Contacts, contact => contact.Label == "Небезопасная ссылка");
    }

    [Fact]
    public void Keeps_contact_with_invalid_uri_as_plain_text_when_within_limit()
    {
        var viewModel = CreateViewModel(
            [new HelpContactOptions { Label = "Ссылка", Value = "file", Uri = "file:///C:/temp" }],
            new RecordingExternalLinkLauncher());

        Assert.Single(viewModel.Contacts);
        Assert.True(viewModel.Contacts[0].IsPlainText);
    }

    [Fact]
    public void Close_command_publishes_dialog_result()
    {
        var viewModel = CreateViewModel([], new RecordingExternalLinkLauncher());
        bool? result = null;
        using var subscription = viewModel.Result.Subscribe(value => result = value);

        viewModel.CloseCommand.Execute().Subscribe();

        Assert.True(result);
    }

    private static HelpDialogViewModel CreateViewModel(
        IEnumerable<HelpContactOptions> contacts,
        IExternalLinkLauncher launcher) =>
        new(
            new StaticHelpOptionsProvider(new HelpOptions { Contacts = contacts.ToList() }),
            launcher,
            NullLogger<HelpDialogViewModel>.Instance);

    private sealed class StaticHelpOptionsProvider(HelpOptions options) : IHelpOptionsProvider
    {
        public HelpOptions GetCurrent() => options;
    }

    private sealed class RecordingExternalLinkLauncher : IExternalLinkLauncher
    {
        public Uri? OpenedUri { get; private set; }

        public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            OpenedUri = uri;
            return Task.CompletedTask;
        }
    }
}
