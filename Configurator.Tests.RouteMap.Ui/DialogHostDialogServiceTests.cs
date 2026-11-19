using System.Reflection;
using System.Reactive.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Configurator.Desktop.Dialogs;
using Configurator.Desktop.Dialogs.ConfirmDialog;
using DialogHostAvalonia;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Configurator.Tests.RouteMap.Ui;

public sealed class DialogHostDialogServiceTests
{
    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Confirm_ReturnsActualUserResult(bool confirm, bool withTitle)
    {
        using var fixture = new DialogFixture();
        var result = withTitle
            ? fixture.Service.ConfirmAsync("Title", "Message", "Accept", "Reject")
            : fixture.Service.ConfirmAsync("Message");
        Dispatcher.UIThread.RunJobs();
        Assert.False(result.IsCompleted);
        Assert.True(fixture.Host.IsOpen);
        var view = fixture.View;
        var viewModel = Assert.IsType<ConfirmDialogViewModel>(view.DataContext);
        ((ICommand)(confirm ? viewModel.OkCommand : viewModel.CancelCommand)).Execute(null);
        Assert.Equal(confirm, await result.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [AvaloniaFact]
    public async Task Confirm_HostClosesWithoutTypedPayload_ReturnsFalse()
    {
        using var fixture = new DialogFixture();
        var result = fixture.Service.ConfirmAsync("Message");
        Dispatcher.UIThread.RunJobs();
        DialogHost.Close(DialogHostIds.Root);
        Assert.False(await result.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [AvaloniaFact]
    public async Task MissingHost_ThrowsDiagnosticExceptionWithOriginalCause()
    {
        using var services = new ServiceCollection().AddTransient<ConfirmDialogView>().BuildServiceProvider();
        var service = new DialogHostDialogService(new DialogViewFactory(services), new DialogCoordinator());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfirmAsync("Message"));
        Assert.Equal($"Dialog host '{DialogHostIds.Root}' was not found. Ensure it is present in MainWindow.", error.Message);
        Assert.IsType<InvalidOperationException>(error.InnerException);
    }

    [AvaloniaFact]
    public async Task CoordinatorCancellation_IsPropagated()
    {
        using var fixture = new DialogFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.ConfirmAsync("Message", cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.False(fixture.Host.IsOpen);
    }

    [AvaloniaFact]
    public async Task OtherCoordinatorException_IsPropagatedUnchanged()
    {
        using var fixture = new DialogFixture();
        var expected = new InvalidOperationException("Injected coordinator subscription failure");
        var context = new DialogViewContext<bool>(new Border(),
            Observable.Create<bool>((Func<IObserver<bool>, IDisposable>)(_ => throw expected)));
        var method = typeof(DialogHostDialogService).GetMethod("ShowWithFallbackAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(bool));
        var result = (Task<bool>)method.Invoke(fixture.Service, [context, CancellationToken.None])!;
        Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(() => result));
        Assert.False(fixture.Host.IsOpen);
    }

    private sealed class DialogFixture : IDisposable
    {
        private readonly DialogHostStyles _styles = new();
        private readonly ServiceProvider _services;
        private readonly Window _window;
        public DialogHost Host { get; } = new() { Identifier = DialogHostIds.Root };
        public ConfirmDialogView View => _services.GetRequiredService<ConfirmDialogView>();
        public DialogHostDialogService Service { get; }

        public DialogFixture()
        {
            Avalonia.Application.Current!.Styles.Add(_styles);
            _services = new ServiceCollection().AddSingleton<ConfirmDialogView>().BuildServiceProvider();
            Service = new DialogHostDialogService(new DialogViewFactory(_services), new DialogCoordinator());
            _window = new Window { Content = Host, Width = 800, Height = 600 };
            _window.Show();
        }

        public void Dispose()
        {
            if (Host.IsOpen) DialogHost.Close(DialogHostIds.Root);
            _window.Close();
            _services.Dispose();
            Avalonia.Application.Current!.Styles.Remove(_styles);
        }
    }
}
