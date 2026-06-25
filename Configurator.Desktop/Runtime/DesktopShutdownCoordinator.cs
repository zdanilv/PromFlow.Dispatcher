using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Configurator.Application.Services.Signals;
using Configurator.Application.Services.Runtime;
using Configurator.Desktop.Main;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Configurator.Desktop.Runtime;

public sealed class DesktopShutdownCoordinator : IDesktopShutdownCoordinator
{
    private readonly MainViewModel _mainViewModel;
    private readonly IApplicationRuntimeCoordinator _runtimeCoordinator;
    private readonly IServiceProvider _serviceProvider;
    private readonly ApplicationLifecycleOptions _options;
    private readonly ILogger<DesktopShutdownCoordinator> _logger;
    private bool _isShutdownInProgress;

    public DesktopShutdownCoordinator(
        MainViewModel mainViewModel,
        IApplicationRuntimeCoordinator runtimeCoordinator,
        IServiceProvider serviceProvider,
        ApplicationLifecycleOptions options,
        ILogger<DesktopShutdownCoordinator> logger)
    {
        _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
        _runtimeCoordinator = runtimeCoordinator ?? throw new ArgumentNullException(nameof(runtimeCoordinator));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options?.Clone() ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsCloseAllowed { get; private set; }

    public async Task RequestShutdownAsync(
        Window? window,
        IClassicDesktopStyleApplicationLifetime? applicationLifetime,
        CancellationToken cancellationToken = default)
    {
        if (_isShutdownInProgress)
        {
            return;
        }

        _isShutdownInProgress = true;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.ShutdownTimeoutSeconds)));

        try
        {
            await _mainViewModel.ShutdownAsync(timeout.Token).ConfigureAwait(false);
            (_serviceProvider.GetService<ISignalValueProvider>() as IDisposable)?.Dispose();
            await _runtimeCoordinator.StopAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Application shutdown timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Application shutdown failed.");
        }
        finally
        {
            IsCloseAllowed = true;
        }

        if (window is not null)
        {
            window.Close();
        }
        else
        {
            applicationLifetime?.Shutdown();
        }
    }
}
