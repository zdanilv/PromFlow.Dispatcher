using Avalonia;
using Avalonia.Markup.Xaml;
using ReactiveUI;
using ReactiveUI.Avalonia;

namespace Configurator.Desktop.Workspace.Archive;

public partial class ArchiveView : ReactiveUserControl<ArchiveViewModel>
{
    private IDisposable? _initializationSubscription;

    public ArchiveView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (ViewModel is not null)
        {
            _initializationSubscription ??= ViewModel.InitializeCommand.Execute().Subscribe();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _initializationSubscription?.Dispose();
        _initializationSubscription = null;
        base.OnDetachedFromVisualTree(e);
    }
}
