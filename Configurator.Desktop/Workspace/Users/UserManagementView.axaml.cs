using Avalonia;
using Avalonia.Markup.Xaml;
using ReactiveUI;
using ReactiveUI.Avalonia;

namespace Configurator.Desktop.Workspace.Users;

public partial class UserManagementView : ReactiveUserControl<UserManagementViewModel>
{
    private IDisposable? _initializationSubscription;

    public UserManagementView()
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
