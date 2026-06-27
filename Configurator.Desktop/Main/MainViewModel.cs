using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Licensing;
using Configurator.Application.Services.Runtime;
using Configurator.Desktop.Workspace;
using Configurator.Desktop.Workspace.Authorization;
using ReactiveUI;
using System.Reactive.Disposables;

namespace Configurator.Desktop.Main;

public sealed class MainViewModel : ViewModelBase, IScreen, IDisposable
{
    private readonly IUserManagementService _userManagementService;
    private readonly ILicenseService _licenseService;
    private readonly IApplicationRuntimeCoordinator _runtimeCoordinator;
    private readonly Func<IScreen, Func<CancellationToken, Task>, AuthorizationViewModel> _authorizationFactory;
    private readonly Func<IScreen, Func<CancellationToken, Task>, AdminBootstrapViewModel> _bootstrapFactory;
    private readonly Func<IScreen, Func<WorkspaceViewModel, CancellationToken, Task>, WorkspaceViewModel> _workspaceFactory;
    private readonly CompositeDisposable _navigationSubscriptions = new();
    private readonly CancellationTokenSource _startupCancellation = new();
    private IRoutableViewModel? _currentViewModel;
    private WorkspaceViewModel? _workspace;
    private bool _disposed;

    public MainViewModel(
        IUserManagementService userManagementService,
        ILicenseService licenseService,
        IApplicationRuntimeCoordinator runtimeCoordinator,
        Func<IScreen, Func<CancellationToken, Task>, AuthorizationViewModel> authorizationFactory,
        Func<IScreen, Func<CancellationToken, Task>, AdminBootstrapViewModel> bootstrapFactory,
        Func<IScreen, Func<WorkspaceViewModel, CancellationToken, Task>, WorkspaceViewModel> workspaceFactory)
    {
        _userManagementService = userManagementService ?? throw new ArgumentNullException(nameof(userManagementService));
        _licenseService = licenseService ?? throw new ArgumentNullException(nameof(licenseService));
        _runtimeCoordinator = runtimeCoordinator ?? throw new ArgumentNullException(nameof(runtimeCoordinator));
        _authorizationFactory = authorizationFactory ?? throw new ArgumentNullException(nameof(authorizationFactory));
        _bootstrapFactory = bootstrapFactory ?? throw new ArgumentNullException(nameof(bootstrapFactory));
        _workspaceFactory = workspaceFactory ?? throw new ArgumentNullException(nameof(workspaceFactory));

        StartupTask = StartAndNavigateAsync(_startupCancellation.Token);
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

        _startupCancellation.Cancel();
        _disposed = true;
        DisposeCurrentViewModel();
        _navigationSubscriptions.Dispose();
        _startupCancellation.Dispose();
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        Dispose();
        return Task.CompletedTask;
    }

    private async Task StartAndNavigateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _runtimeCoordinator.StartAsync(cancellationToken);
            await NavigateStartupAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            NavigateTo(new StartupFailureViewModel(this, ex.Message));
        }
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

    private async Task ShowLoginAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var viewModel = _authorizationFactory(this, ShowWorkspaceAsync);
        NavigateTo(viewModel);
        await viewModel.InitializeAsync(_startupCancellation.Token);
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
