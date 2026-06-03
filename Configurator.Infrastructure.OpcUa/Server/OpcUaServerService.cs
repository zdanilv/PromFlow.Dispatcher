using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;
using Configurator.Infrastructure.OpcUa.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Configuration;

namespace Configurator.Infrastructure.OpcUa.Server;

/// <summary>
/// Управляет встроенным OPC UA сервером: стартом, остановкой и публикацией значений тегов.
/// </summary>
public sealed class OpcUaServerService : IOpcUaServerService, IOpcUaServerTagUpdater, IAsyncDisposable
{
    private readonly OpcUaApplicationConfigurationFactory _configurationFactory;
    private readonly IOpcUaServerCommandSink _commandSink;
    private readonly ILogger<OpcUaServerService> _logger;
    private readonly IOptionsMonitor<OpcUaOptions> _optionsMonitor;
    private readonly ITelemetryContext _telemetry = DefaultTelemetry.Create(_ => { });
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _optionsLock = new();
    private ApplicationInstance? _application;
    private OpcUaOptions _options;
    private OpcUaStandardServer? _server;

    /// <summary>
    /// Создает сервис встроенного OPC UA сервера и связывает его с настройками приложения.
    /// </summary>
    public OpcUaServerService(
        IOptionsMonitor<OpcUaOptions> optionsMonitor,
        OpcUaApplicationConfigurationFactory configurationFactory,
        IOpcUaServerCommandStateProvider commandStateProvider,
        ILogger<OpcUaServerService> logger)
    {
        _optionsMonitor = optionsMonitor;
        _configurationFactory = configurationFactory;
        _commandSink = commandStateProvider as IOpcUaServerCommandSink
            ?? throw new InvalidOperationException("Command state provider must support server command publication.");
        _logger = logger;
        _options = optionsMonitor.CurrentValue.Clone();
    }

    /// <summary>
    /// Запускает выбранные роли runtime с актуальными настройками.
    /// </summary>
    public async Task<OpcUaOperationResult> StartAsync(
        CancellationToken cancellationToken = default,
        OpcUaOptions? options = null)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);

        try
        {
            var currentOptions = SetOptions(options);
            if (!IsNoneSecurity(currentOptions.Security.Mode) || currentOptions.Security.Certificates.Enabled)
            {
                return OpcUaOperationResult.Failure(
                    "SecurityNotSupported",
                    "Only Security.Mode=None and Certificates.Enabled=false are supported in v1.");
            }

            if (_application is not null)
            {
                return OpcUaOperationResult.Success();
            }

            try
            {
                var configuration = _configurationFactory.CreateServer(currentOptions);
                _server = new OpcUaStandardServer(currentOptions.Nodes, _commandSink);
                _application = new ApplicationInstance(configuration, _telemetry)
                {
                    ApplicationName = currentOptions.Server.ApplicationName,
                    ApplicationType = ApplicationType.Server,
                    ApplicationConfiguration = configuration
                };

                await _application.StartAsync(_server);
                _logger.LogInformation("OPC UA server started at {EndpointUrl}", currentOptions.Server.EndpointUrl);
                return OpcUaOperationResult.Success();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to start OPC UA server");
                _application = null;
                _server = null;
                return ToFailure(ex, "ServerStartFailed", "Failed to start OPC UA server.");
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Останавливает активные роли runtime и освобождает сетевые ресурсы.
    /// </summary>
    public async Task<OpcUaOperationResult> StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);

        try
        {
            if (_application is null)
            {
                return OpcUaOperationResult.Success();
            }

            try
            {
                await _application.StopAsync();
                _application = null;
                _server = null;
                _logger.LogInformation("OPC UA server stopped");
                return OpcUaOperationResult.Success();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return ToFailure(ex, "ServerStopFailed", "Failed to stop OPC UA server.");
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Передает новое значение тега в NodeManager запущенного сервера.
    /// </summary>
    public async Task<OpcUaOperationResult> UpdateTagValueAsync(
        OpcUaTagValue value,
        CancellationToken cancellationToken = default)
    {
        if (_server is null)
        {
            return OpcUaOperationResult.Failure(
                "ServerNotStarted",
                "OPC UA server is not started.");
        }

        try
        {
            var nodeManager = await _server.WaitForNodeManagerAsync(cancellationToken);
            return nodeManager.UpdateTagValue(value.Address, value.Value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToFailure(ex, "ServerTagUpdateFailed", "Failed to update tag value on OPC UA server.");
        }
    }

    /// <summary>
    /// Отписывается от событий, останавливает runtime и освобождает синхронизационные ресурсы.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifecycleGate.Dispose();
    }

    /// <summary>
    /// Сохраняет снимок настроек, с которыми будет запущен сервер.
    /// </summary>
    private OpcUaOptions SetOptions(OpcUaOptions? options)
    {
        var next = (options ?? _optionsMonitor.CurrentValue).Clone();

        lock (_optionsLock)
        {
            _options = next;
        }

        return next;
    }

    /// <summary>
    /// Проверяет, что сервер запускается в поддерживаемом режиме None.
    /// </summary>
    private static bool IsNoneSecurity(string mode)
        => string.Equals(mode, "None", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Преобразует исключение сервера в единый результат ошибки OPC UA.
    /// </summary>
    private static OpcUaOperationResult ToFailure(Exception exception, string code, string message)
        => OpcUaOperationResult.Failure(code, message, exception.Message);
}
