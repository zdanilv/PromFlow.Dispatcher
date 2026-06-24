using Configurator.Desktop.Workspace;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Configurator.Desktop.Main;

public partial class MainViewModel : ViewModelBase, IScreen, IDisposable
{
    private readonly WorkspaceViewModel _workspace;

    public RoutingState Router { get; } = new RoutingState();

    [Reactive] private string? _label = "Test VM";

    public MainViewModel(Func<IScreen, WorkspaceViewModel> workspaceFactory)
    {
        _workspace = workspaceFactory(this);
        Router.Navigate.Execute(_workspace).Subscribe();
    }

    public void Dispose() => _workspace.Dispose();
}
