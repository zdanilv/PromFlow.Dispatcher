using System.Collections.ObjectModel;
using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Runtime;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Configurator.Desktop.Workspace;

public sealed class WorkspaceViewModel : ViewModelBase, IRoutableViewModel, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IReadOnlyList<WorkspaceTabDescriptor> _tabDescriptors;
    private readonly IAccessDecisionService _accessDecisionService;
    private readonly IAuthenticationService _authenticationService;
    private readonly IModbusRuntimeService _modbusRuntime;
    private readonly IModbusDemoOptionsProvider _modbusOptions;
    private readonly ILogger<WorkspaceViewModel> _logger;
    private readonly Func<WorkspaceViewModel, CancellationToken, Task> _logoutRequested;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private bool _initialized;
    private bool _disposed;

    public WorkspaceViewModel(
        IScreen hostScreen,
        IServiceProvider serviceProvider,
        IUserSessionAccessor sessionAccessor,
        IEnumerable<WorkspaceTabDescriptor> tabDescriptors,
        IAccessDecisionService accessDecisionService,
        IAuthenticationService authenticationService,
        IModbusRuntimeService modbusRuntime,
        IModbusDemoOptionsProvider modbusOptions,
        ILogger<WorkspaceViewModel> logger,
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
        _modbusRuntime = modbusRuntime ?? throw new ArgumentNullException(nameof(modbusRuntime));
        _modbusOptions = modbusOptions ?? throw new ArgumentNullException(nameof(modbusOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> LogoutCommand { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
        {
            return;
        }

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
            throw new UnauthorizedAccessException("Authenticated user has no workspace permissions.");
        }

        _initialized = true;

        var options = _modbusOptions.CurrentValue.Clone();
        if (options.AutostartOnWorkspaceOpen && options.StartupMode != ModbusRunMode.None)
        {
            _ = StartModbusAsync(_modbusRuntime, options, _logger, _lifetimeCancellation.Token);
        }
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

    private static async Task StartModbusAsync(
        IModbusRuntimeService runtime,
        ModbusOptions options,
        ILogger<WorkspaceViewModel> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await runtime.StartAsync(options.StartupMode, options, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to autostart Modbus in {StartupMode} mode.",
                options.StartupMode);
        }
    }
}
