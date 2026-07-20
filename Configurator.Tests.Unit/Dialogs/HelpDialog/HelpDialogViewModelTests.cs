using System.Reactive.Linq;
using Configurator.Desktop.Dialogs.HelpDialog;
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
    public async Task Exposes_template_contacts_and_opens_website()
    {
        var launcher = new RecordingExternalLinkLauncher();
        var viewModel = new HelpDialogViewModel(launcher);

        Assert.Equal("8 953 448 31 16", HelpDialogViewModel.Phone);
        Assert.Equal("example@example.com", HelpDialogViewModel.Email);
        Assert.Equal("example.com", HelpDialogViewModel.Website);

        await viewModel.OpenWebsiteCommand.Execute().FirstAsync();

        Assert.Equal(new Uri("https://example.com"), launcher.OpenedUri);
    }

    [Fact]
    public void Close_command_publishes_dialog_result()
    {
        var viewModel = new HelpDialogViewModel(new RecordingExternalLinkLauncher());
        bool? result = null;
        using var subscription = viewModel.Result.Subscribe(value => result = value);

        viewModel.CloseCommand.Execute().Subscribe();

        Assert.True(result);
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
