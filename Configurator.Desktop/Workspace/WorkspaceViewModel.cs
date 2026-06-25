using System.Collections.ObjectModel;
using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Licensing;
using ReactiveUI;

namespace Configurator.Desktop.Workspace;

public sealed class WorkspaceViewModel : ViewModelBase, IRoutableViewModel, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IReadOnlyList<WorkspaceTabDescriptor> _tabDescriptors;
    private readonly IAccessDecisionService _accessDecisionService;
    private readonly IAuthenticationService _authenticationService;
    private readonly ILicenseService _licenseService;
    private readonly Func<WorkspaceViewModel, CancellationToken, Task> _logoutRequested;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private bool _initialized;
    private bool _hasTabs;
    private bool _hasNoTabs;
    private WorkspaceAccessUnavailableViewModel? _accessUnavailable;
    private bool _disposed;

    public WorkspaceViewModel(
        IScreen hostScreen,
        IServiceProvider serviceProvider,
        IUserSessionAccessor sessionAccessor,
        IEnumerable<WorkspaceTabDescriptor> tabDescriptors,
        IAccessDecisionService accessDecisionService,
        IAuthenticationService authenticationService,
        ILicenseService licenseService,
        Func<WorkspaceViewModel, CancellationToken, Task> logoutRequested)
    {
        ArgumentNullException.ThrowIfNull(sessionAccessor);

        HostScreen = hostScreen;
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _tabDescriptors = (tabDescriptors ?? throw new ArgumentNullException(nameof(tabDescriptors)))
            .OrderBy(descriptor => descriptor.Order)
            .ToArray();
        _accessDecisionService = accessDecisionService ?? throw new ArgumentNullException(nameof(accessDecisionService));
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
        _licenseService = licenseService ?? throw new ArgumentNullException(nameof(licenseService));
        _logoutRequested = logoutRequested ?? throw new ArgumentNullException(nameof(logoutRequested));
        AuthToken = sessionAccessor.Current.IsAuthenticated.ToString();

        LogoutCommand = ReactiveCommand.CreateFromTask(
            () => LogoutAsync(_lifetimeCancellation.Token));
    }

    public string Name { get; set; } = "Work Page";

    public string UrlPathSegment => "main";

    public IScreen HostScreen { get; }

    public string AuthToken { get; }

    public ObservableCollection<WorkspaceTabViewModel> Tabs { get; } = [];

    public WorkspaceAccessUnavailableViewModel? AccessUnavailable
    {
        get => _accessUnavailable;
        private set => this.RaiseAndSetIfChanged(ref _accessUnavailable, value);
    }

    public bool HasTabs
    {
        get => _hasTabs;
        private set => this.RaiseAndSetIfChanged(ref _hasTabs, value);
    }

    public bool HasNoTabs
    {
        get => _hasNoTabs;
        private set => this.RaiseAndSetIfChanged(ref _hasNoTabs, value);
    }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> LogoutCommand { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
        {
            return;
        }

        await _licenseService.RefreshAsync(cancellationToken);

        foreach (var descriptor in _tabDescriptors)
        {
            var decision = await _accessDecisionService
                .AuthorizeAsync(
                    new AccessRequirement(
                        descriptor.RequiredPermission,
                        descriptor.RequiredLicenseFeature),
                    cancellationToken);
            if (!decision.Succeeded)
            {
                continue;
            }

            Tabs.Add(new WorkspaceTabViewModel(
                descriptor.Id,
                descriptor.Header,
                descriptor.Factory(_serviceProvider)));
        }

        if (Tabs.Count == 0)
        {
            AccessUnavailable = new WorkspaceAccessUnavailableViewModel();
            HasTabs = false;
            HasNoTabs = true;
            _initialized = true;
            return;
        }

        HasTabs = true;
        HasNoTabs = false;
        _initialized = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();

        foreach (var content in Tabs.Select(tab => tab.Content).OfType<IDisposable>().ToArray())
        {
            content.Dispose();
        }

        Tabs.Clear();
    }

    private async Task LogoutAsync(CancellationToken cancellationToken)
    {
        await _authenticationService.SignOutAsync(cancellationToken);
        await _logoutRequested(this, cancellationToken);
    }

}
