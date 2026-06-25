using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Licensing;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Runtime;
using Microsoft.Extensions.Logging;

namespace Configurator.Desktop.Runtime;

public sealed class ApplicationRuntimeCoordinator :
    IApplicationRuntimeCoordinator,
    IApplicationRuntimeStateAccessor
{
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly IApplicationInstanceGuard _instanceGuard;
    private readonly IPersistenceInitializer _persistenceInitializer;
    private readonly IInstallationIdentityService _installationIdentityService;
    private readonly ILicenseService _licenseService;
    private readonly IArchiveRuntime _archiveRuntime;
    private readonly IModbusArchiveCollector _archiveCollector;
    private readonly IModbusRuntimeService _modbusRuntime;
    private readonly IModbusDemoTcpService _modbusDemoTcpService;
    private readonly IModbusDemoOptionsProvider _modbusOptions;
    private readonly ApplicationLifecycleOptions _options;
    private readonly ILogger<ApplicationRuntimeCoordinator> _logger;
    private readonly List<ApplicationRuntimeStepResult> _startupSteps = [];
    private readonly List<ApplicationRuntimeStepResult> _shutdownSteps = [];
    private ApplicationRuntimeSnapshot _current = ApplicationRuntimeSnapshot.Initial;
    private IApplicationInstanceLease? _instanceLease;
    private bool _archiveStartSucceeded;
    private bool _archiveCollectorStartSucceeded;

    public ApplicationRuntimeCoordinator(
        IApplicationInstanceGuard instanceGuard,
        IPersistenceInitializer persistenceInitializer,
        IInstallationIdentityService installationIdentityService,
        ILicenseService licenseService,
        IArchiveRuntime archiveRuntime,
        IModbusArchiveCollector archiveCollector,
        IModbusRuntimeService modbusRuntime,
        IModbusDemoTcpService modbusDemoTcpService,
        IModbusDemoOptionsProvider modbusOptions,
        ApplicationLifecycleOptions options,
        ILogger<ApplicationRuntimeCoordinator> logger)
    {
        _instanceGuard = instanceGuard ?? throw new ArgumentNullException(nameof(instanceGuard));
        _persistenceInitializer = persistenceInitializer ?? throw new ArgumentNullException(nameof(persistenceInitializer));
        _installationIdentityService = installationIdentityService ?? throw new ArgumentNullException(nameof(installationIdentityService));
        _licenseService = licenseService ?? throw new ArgumentNullException(nameof(licenseService));
        _archiveRuntime = archiveRuntime ?? throw new ArgumentNullException(nameof(archiveRuntime));
        _archiveCollector = archiveCollector ?? throw new ArgumentNullException(nameof(archiveCollector));
        _modbusRuntime = modbusRuntime ?? throw new ArgumentNullException(nameof(modbusRuntime));
        _modbusDemoTcpService = modbusDemoTcpService ?? throw new ArgumentNullException(nameof(modbusDemoTcpService));
        _modbusOptions = modbusOptions ?? throw new ArgumentNullException(nameof(modbusOptions));
        _options = options?.Clone() ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public event EventHandler<ApplicationRuntimeStateChangedEventArgs>? StateChanged;

    public ApplicationRuntimeSnapshot Current => _current;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_current.Phase == ApplicationRuntimePhase.Running)
            {
                return;
            }

            _startupSteps.Clear();
            _shutdownSteps.Clear();
            Publish(ApplicationRuntimePhase.Starting);

            using var timeout = CreateTimeout(cancellationToken, _options.StartupTimeoutSeconds);
            var startupToken = timeout.Token;

            try
            {
                _instanceLease = await _instanceGuard.AcquireAsync(startupToken).ConfigureAwait(false);
                AddStartupSuccess("SingleInstance");

                var persistence = await _persistenceInitializer
                    .InitializeAsync(startupToken)
                    .ConfigureAwait(false);
                _startupSteps.AddRange(persistence.Steps);
                Publish(ApplicationRuntimePhase.Starting);
                if (persistence.HasFatalFailure)
                {
                    throw new ApplicationRuntimeException(
                        "Fatal persistence initialization failure.",
                        _startupSteps.ToArray());
                }

                await ExecuteStartupNonFatalAsync(
                    "InstallationIdentity",
                    async token => await _installationIdentityService.GetOrCreateAsync(token).ConfigureAwait(false),
                    startupToken).ConfigureAwait(false);

                await ExecuteStartupNonFatalAsync(
                    "LicenseRefresh",
                    async token => await _licenseService.RefreshAsync(token).ConfigureAwait(false),
                    startupToken).ConfigureAwait(false);

                _archiveStartSucceeded = await ExecuteArchiveStartupStepAsync(
                    "ArchiveRuntimeStart",
                    token => _archiveRuntime.StartAsync(token),
                    startupToken).ConfigureAwait(false);

                if (_archiveStartSucceeded)
                {
                    _archiveCollectorStartSucceeded = await ExecuteArchiveStartupStepAsync(
                        "ModbusArchiveCollectorStart",
                        token => _archiveCollector.StartAsync(token),
                        startupToken).ConfigureAwait(false);
                }
                else
                {
                    AddStartupFailure(
                        "ModbusArchiveCollectorStart",
                        "ArchiveRuntimeUnavailable",
                        "Modbus archive collector was not started because archive runtime startup failed.",
                        isFatal: false);
                }

                await ExecuteModbusAutostartAsync(startupToken).ConfigureAwait(false);

                Publish(ApplicationRuntimePhase.Running);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Application runtime startup failed.");
                Publish(ApplicationRuntimePhase.Faulted);
                await DisposeInstanceLeaseAsync().ConfigureAwait(false);

                if (ex is ApplicationRuntimeException)
                {
                    throw;
                }

                throw new ApplicationRuntimeException(
                    "Application runtime startup failed.",
                    _startupSteps.ToArray(),
                    ex);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_current.Phase == ApplicationRuntimePhase.Stopped)
            {
                return;
            }

            _shutdownSteps.Clear();
            Publish(ApplicationRuntimePhase.ShuttingDown);

            await ExecuteShutdownModbusStopAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteArchiveShutdownStepAsync(
                "ModbusArchiveCollectorStop",
                _archiveCollectorStartSucceeded,
                token => _archiveCollector.StopAsync(token),
                cancellationToken).ConfigureAwait(false);
            await ExecuteArchiveFlushAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteArchiveShutdownStepAsync(
                "ArchiveRuntimeStop",
                _archiveStartSucceeded,
                token => _archiveRuntime.StopAsync(token),
                cancellationToken).ConfigureAwait(false);
            await ExecuteShutdownNonFatalAsync(
                "SingleInstanceRelease",
                async _ => await DisposeInstanceLeaseAsync().ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);

            _archiveStartSucceeded = false;
            _archiveCollectorStartSucceeded = false;
            Publish(ApplicationRuntimePhase.Stopped);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task ExecuteModbusAutostartAsync(CancellationToken cancellationToken)
    {
        var options = _modbusOptions.CurrentValue.Clone();
        if (!options.AutostartOnWorkspaceOpen || options.StartupMode == ModbusRunMode.None)
        {
            AddStartupSuccess("ModbusAutostartSkipped");
            return;
        }

        await ExecuteStartupNonFatalAsync(
            "ModbusAutostart",
            async token => await _modbusRuntime.StartAsync(options.StartupMode, options, token).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteShutdownModbusStopAsync(CancellationToken cancellationToken)
    {
        using var timeout = CreateTimeout(cancellationToken, _options.ModbusStopTimeoutSeconds);
        try
        {
            var result = await _modbusDemoTcpService.StopAsync(timeout.Token).ConfigureAwait(false);
            if (result.Succeeded)
            {
                AddShutdownSuccess("ModbusRuntimeStop");
                return;
            }

            AddShutdownFailure(
                "ModbusRuntimeStop",
                result.ErrorCode ?? "ModbusStopFailed",
                result.ErrorMessage ?? "Modbus runtime stop failed.");
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
        {
            AddShutdownFailure("ModbusRuntimeStop", "ModbusStopFailed", ex.Message);
        }
    }

    private async Task ExecuteArchiveFlushAsync(CancellationToken cancellationToken)
    {
        if (!_archiveStartSucceeded)
        {
            AddShutdownSuccess("ArchiveRuntimeFlushSkipped");
            return;
        }

        using var timeout = CreateTimeout(cancellationToken, _options.ArchiveFlushTimeoutSeconds);
        await ExecuteArchiveShutdownStepAsync(
            "ArchiveRuntimeFlush",
            shouldRun: true,
            token => _archiveRuntime.FlushAsync(token),
            timeout.Token).ConfigureAwait(false);
    }

    private async Task<bool> ExecuteArchiveStartupStepAsync(
        string step,
        Func<CancellationToken, Task<ArchiveOperationResult>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await action(cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
            {
                AddStartupSuccess(step);
                return true;
            }

            AddStartupFailure(
                step,
                result.ErrorCode ?? step + "Failed",
                result.ErrorMessage ?? step + " failed.",
                isFatal: false);
            return false;
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
        {
            AddStartupFailure(step, step + "Failed", ex.Message, isFatal: false);
            return false;
        }
    }

    private async Task ExecuteArchiveShutdownStepAsync(
        string step,
        bool shouldRun,
        Func<CancellationToken, Task<ArchiveOperationResult>> action,
        CancellationToken cancellationToken)
    {
        if (!shouldRun)
        {
            AddShutdownSuccess(step + "Skipped");
            return;
        }

        try
        {
            var result = await action(cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
            {
                AddShutdownSuccess(step);
                return;
            }

            AddShutdownFailure(
                step,
                result.ErrorCode ?? step + "Failed",
                result.ErrorMessage ?? step + " failed.");
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
        {
            AddShutdownFailure(step, step + "Failed", ex.Message);
        }
    }

    private async Task ExecuteStartupNonFatalAsync(
        string step,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await action(cancellationToken).ConfigureAwait(false);
            AddStartupSuccess(step);
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            AddStartupFailure(step, step + "Failed", ex.Message, isFatal: false);
        }
    }

    private async Task ExecuteShutdownNonFatalAsync(
        string step,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await action(cancellationToken).ConfigureAwait(false);
            AddShutdownSuccess(step);
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            AddShutdownFailure(step, step + "Failed", ex.Message);
        }
    }

    private void AddStartupSuccess(string step)
    {
        _startupSteps.Add(ApplicationRuntimeStepResult.Success(step));
        Publish(ApplicationRuntimePhase.Starting);
    }

    private void AddStartupFailure(string step, string errorCode, string errorMessage, bool isFatal)
    {
        _startupSteps.Add(ApplicationRuntimeStepResult.Failure(step, errorCode, errorMessage, isFatal));
        Publish(ApplicationRuntimePhase.Starting);
        _logger.LogWarning(
            "Application runtime startup step {Step} failed: {ErrorCode} {ErrorMessage}",
            step,
            errorCode,
            errorMessage);
    }

    private void AddShutdownSuccess(string step)
    {
        _shutdownSteps.Add(ApplicationRuntimeStepResult.Success(step));
        Publish(ApplicationRuntimePhase.ShuttingDown);
    }

    private void AddShutdownFailure(string step, string errorCode, string errorMessage)
    {
        _shutdownSteps.Add(ApplicationRuntimeStepResult.Failure(step, errorCode, errorMessage, isFatal: false));
        Publish(ApplicationRuntimePhase.ShuttingDown);
        _logger.LogWarning(
            "Application runtime shutdown step {Step} failed: {ErrorCode} {ErrorMessage}",
            step,
            errorCode,
            errorMessage);
    }

    private async Task DisposeInstanceLeaseAsync()
    {
        var lease = Interlocked.Exchange(ref _instanceLease, null);
        if (lease is not null)
        {
            await lease.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void Publish(ApplicationRuntimePhase phase)
    {
        var previous = _current;
        var current = new ApplicationRuntimeSnapshot(
            phase,
            _startupSteps.ToArray(),
            _shutdownSteps.ToArray());
        _current = current;
        StateChanged?.Invoke(this, new ApplicationRuntimeStateChangedEventArgs(previous, current));
    }

    private static CancellationTokenSource CreateTimeout(
        CancellationToken cancellationToken,
        int timeoutSeconds)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
        return timeout;
    }
}
