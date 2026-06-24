using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Licensing;
using Configurator.Desktop.Workspace;
using Configurator.Desktop.Workspace.Authorization;
using ReactiveUI;
using System.Reactive.Disposables;

namespace Configurator.Desktop.Main;

public sealed class MainViewModel : ViewModelBase, IScreen, IDisposable
{
    private readonly IUserManagementService _userManagementService;
    private readonly ILicenseService _licenseService;
    private readonly Func<IScreen, Func<CancellationToken, Task>, AuthorizationViewModel> _authorizationFactory;
    private readonly Func<IScreen, Func<CancellationToken, Task>, AdminBootstrapViewModel> _bootstrapFactory;
    private readonly Func<IScreen, Func<WorkspaceViewModel, CancellationToken, Task>, WorkspaceViewModel> _workspaceFactory;
    private readonly CompositeDisposable _navigationSubscriptions = new();
    private IRoutableViewModel? _currentViewModel;
    private WorkspaceViewModel? _workspace;
    private bool _disposed;

    public MainViewModel(
        IUserManagementService userManagementService,
        ILicenseService licenseService,
        Func<IScreen, Func<CancellationToken, Task>, AuthorizationViewModel> authorizationFactory,
        Func<IScreen, Func<CancellationToken, Task>, AdminBootstrapViewModel> bootstrapFactory,
        Func<IScreen, Func<WorkspaceViewModel, CancellationToken, Task>, WorkspaceViewModel> workspaceFactory)
    {
        _userManagementService = userManagementService ?? throw new ArgumentNullException(nameof(userManagementService));
        _licenseService = licenseService ?? throw new ArgumentNullException(nameof(licenseService));
        _authorizationFactory = authorizationFactory ?? throw new ArgumentNullException(nameof(authorizationFactory));
        _bootstrapFactory = bootstrapFactory ?? throw new ArgumentNullException(nameof(bootstrapFactory));
        _workspaceFactory = workspaceFactory ?? throw new ArgumentNullException(nameof(workspaceFactory));

        StartupTask = NavigateStartupAsync(CancellationToken.None);
    }

    public RoutingState Router { get; } = new();

    public Task StartupTask { get; }

    public IRoutableViewModel? CurrentViewModel
    {
        get => _currentViewModel;
        private set => this.RaiseAndSetIfChanged(ref _currentViewModel, value);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeCurrentViewModel();
        _navigationSubscriptions.Dispose();
    }

    private async Task NavigateStartupAsync(CancellationToken cancellationToken)
    {
        var bootstrapRequired = await _userManagementService
            .IsBootstrapRequiredAsync(cancellationToken);
        if (bootstrapRequired)
        {
            await ShowBootstrapAsync(cancellationToken);
            return;
        }

        await ShowLoginAsync(cancellationToken);
    }

    private Task ShowBootstrapAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NavigateTo(_bootstrapFactory(this, ShowLoginAsync));
        return Task.CompletedTask;
    }

    private Task ShowLoginAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NavigateTo(_authorizationFactory(this, ShowWorkspaceAsync));
        return Task.CompletedTask;
    }

    private async Task ShowWorkspaceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var workspace = _workspaceFactory(this, OnWorkspaceLogoutAsync);
        try
        {
            await _licenseService.RefreshAsync(cancellationToken);
            await workspace.InitializeAsync(cancellationToken);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }

        _workspace = workspace;
        NavigateTo(workspace);
    }

    private async Task OnWorkspaceLogoutAsync(
        WorkspaceViewModel workspace,
        CancellationToken cancellationToken)
    {
        if (ReferenceEquals(_workspace, workspace))
        {
            _workspace = null;
        }

        await ShowLoginAsync(cancellationToken);
    }

    private void NavigateTo(IRoutableViewModel viewModel)
    {
        DisposeCurrentViewModel();
        CurrentViewModel = viewModel;
        _navigationSubscriptions.Add(Router.Navigate.Execute(viewModel).Subscribe());
    }

    private void DisposeCurrentViewModel()
    {
        if (CurrentViewModel is IDisposable disposable)
        {
            disposable.Dispose();
        }

        CurrentViewModel = null;
    }
}
