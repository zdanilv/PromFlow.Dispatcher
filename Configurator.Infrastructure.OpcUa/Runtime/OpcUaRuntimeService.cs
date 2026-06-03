using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Configurator.Infrastructure.OpcUa.Runtime;

/// <summary>
/// Координирует OPC UA клиент и сервер, собирая их статусы и значения в единый runtime для UI.
/// </summary>
internal sealed class OpcUaRuntimeService : IOpcUaRuntimeService
{
    private readonly IOpcUaClientService _clientService;
    private readonly IOpcUaConnectionStateProvider _connectionStateProvider;
    private readonly ILogger<OpcUaRuntimeService> _logger;
    private readonly IOptionsMonitor<OpcUaOptions> _optionsMonitor;
    private readonly IOpcUaServerCommandStateProvider _serverCommandStateProvider;
    private readonly IOpcUaServerService _serverService;
    private readonly IOpcUaServerTagUpdater _serverTagUpdater;
    private readonly IOpcUaTagWriter _tagWriter;
    // Все команды запуска и остановки проходят через gate, чтобы не пересечь lifecycle SDK-сессии и сервера.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private readonly Dictionary<string, OpcUaTagValue> _clientCommands = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OpcUaTagValue> _clientTelemetry = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OpcUaTagValue> _serverCommands = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OpcUaTagValue> _serverTelemetry = new(StringComparer.Ordinal);
    private readonly List<IDisposable> _clientSubscriptions = [];
    private readonly IDisposable _clientStateSubscription;
    private readonly IDisposable _serverCommandSubscription;
    private CancellationTokenSource? _demoTelemetryCancellation;
    private Task? _demoTelemetryTask;
    private int _serverCounter;
    private OpcUaSnapshot _clientSnapshot = OpcUaSnapshot.Empty;
    private OpcUaSnapshot _serverSnapshot = OpcUaSnapshot.Empty;
    private OpcUaStatus _status = OpcUaStatus.Stopped;

    /// <summary>
    /// Создает runtime и подписывает его на состояния клиента и command-поток встроенного сервера.
    /// </summary>
    public OpcUaRuntimeService(
        IOpcUaClientService clientService,
        IOpcUaConnectionStateProvider connectionStateProvider,
        IOpcUaServerService serverService,
        IOpcUaServerTagUpdater serverTagUpdater,
        IOpcUaServerCommandStateProvider serverCommandStateProvider,
        IOpcUaTagWriter tagWriter,
        IOptionsMonitor<OpcUaOptions> optionsMonitor,
        ILogger<OpcUaRuntimeService> logger)
    {
        _clientService = clientService;
        _connectionStateProvider = connectionStateProvider;
        _serverService = serverService;
        _serverTagUpdater = serverTagUpdater;
        _serverCommandStateProvider = serverCommandStateProvider;
        _tagWriter = tagWriter;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
        CurrentOptions = _optionsMonitor.CurrentValue.Clone();

        _clientStateSubscription = _connectionStateProvider.ConnectionStates.Subscribe(
            new ActionObserver<OpcUaConnectionState>(OnClientConnectionStateChanged));
        _serverCommandSubscription = _serverCommandStateProvider.CommandChanges.Subscribe(
            new ActionObserver<OpcUaTagValue>(PublishServerCommand));
    }

    /// <summary>
    /// Агрегированный статус клиентской и серверной ролей OPC UA.
    /// </summary>
    public OpcUaStatus Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    /// <summary>
    /// Последний snapshot, опубликованный клиентской ролью OPC UA.
    /// </summary>
    public OpcUaSnapshot ClientSnapshot
    {
        get
        {
            lock (_sync)
            {
                return _clientSnapshot;
            }
        }
    }

    /// <summary>
    /// Последний snapshot, опубликованный серверной ролью OPC UA.
    /// </summary>
    public OpcUaSnapshot ServerSnapshot
    {
        get
        {
            lock (_sync)
            {
                return _serverSnapshot;
            }
        }
    }

    /// <summary>
    /// Копия настроек, с которыми runtime был запущен или перезапущен.
    /// </summary>
    public OpcUaOptions CurrentOptions { get; private set; }

    /// <summary>
    /// Уведомляет подписчиков об изменении агрегированного статуса runtime.
    /// </summary>
    public event EventHandler<OpcUaStatus>? StatusChanged;

    /// <summary>
    /// Уведомляет подписчиков о новом snapshot клиентской или серверной роли.
    /// </summary>
    public event EventHandler<OpcUaSnapshot>? SnapshotChanged;

    /// <summary>
    /// Запускает выбранные роли runtime с актуальными настройками.
    /// </summary>
    public async Task StartAsync(
        OpcUaRunMode mode = OpcUaRunMode.Both,
        OpcUaOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            CurrentOptions = (options ?? _optionsMonitor.CurrentValue).Clone();

            switch (mode)
            {
                case OpcUaRunMode.None:
                    await StopBothCoreAsync(cancellationToken);
                    break;
                case OpcUaRunMode.Client:
                    await StopServerCoreAsync(cancellationToken);
                    await StartClientCoreAsync(cancellationToken);
                    break;
                case OpcUaRunMode.Server:
                    await StopClientCoreAsync(cancellationToken);
                    await StartServerCoreAsync(cancellationToken);
                    break;
                case OpcUaRunMode.Both:
                    await StartModeCoreAsync(OpcUaRunMode.Both, cancellationToken);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to start OPC UA runtime");
            PublishStatus(new OpcUaStatus
            {
                ClientState = Status.ClientState,
                ServerState = Status.ServerState,
                ClientMessage = Status.ClientMessage,
                ServerMessage = Status.ServerMessage,
                LastError = ex.Message,
                UpdatedAt = DateTimeOffset.Now
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Останавливает активные роли runtime и освобождает сетевые ресурсы.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await StopBothCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Останавливает текущие роли runtime и запускает их заново с переданными или текущими настройками.
    /// </summary>
    public async Task RestartAsync(
        OpcUaRunMode mode = OpcUaRunMode.Both,
        OpcUaOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await StopBothCoreAsync(cancellationToken);
            CurrentOptions = (options ?? _optionsMonitor.CurrentValue).Clone();
            await StartModeCoreAsync(mode, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Запускает только клиентскую роль runtime.
    /// </summary>
    public async Task StartClientAsync(OpcUaOptions? options = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            CurrentOptions = (options ?? _optionsMonitor.CurrentValue).Clone();
            await StartClientCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Останавливает только клиентскую роль runtime.
    /// </summary>
    public Task StopClientAsync(CancellationToken cancellationToken = default)
        => ExecuteSingleRoleAsync(() => StopClientCoreAsync(cancellationToken), cancellationToken);

    /// <summary>
    /// Перезапускает клиентскую роль runtime с переданными или текущими настройками.
    /// </summary>
    public async Task RestartClientAsync(OpcUaOptions? options = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            CurrentOptions = (options ?? _optionsMonitor.CurrentValue).Clone();
            await StopClientCoreAsync(cancellationToken);
            await StartClientCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Запускает только серверную роль runtime.
    /// </summary>
    public async Task StartServerAsync(OpcUaOptions? options = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            CurrentOptions = (options ?? _optionsMonitor.CurrentValue).Clone();
            await StartServerCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Останавливает только серверную роль runtime.
    /// </summary>
    public Task StopServerAsync(CancellationToken cancellationToken = default)
        => ExecuteSingleRoleAsync(() => StopServerCoreAsync(cancellationToken), cancellationToken);

    /// <summary>
    /// Перезапускает серверную роль runtime с переданными или текущими настройками.
    /// </summary>
    public async Task RestartServerAsync(OpcUaOptions? options = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            CurrentOptions = (options ?? _optionsMonitor.CurrentValue).Clone();
            await StopServerCoreAsync(cancellationToken);
            await StartServerCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Записывает command-тег через клиентскую роль и публикует результат в client snapshot.
    /// </summary>
    public async Task<OpcUaOperationResult> WriteClientTagAsync(
        OpcUaTagWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _tagWriter.WriteTagAsync(request, cancellationToken);
        if (result.Succeeded)
        {
            PublishClientCommand(new OpcUaTagValue(
                request.Address,
                request.Value,
                DateTimeOffset.Now,
                "Good"));
            return result;
        }

        PublishClientCommand(CreateErrorValue(
            request.Address,
            result.Error?.Message ?? "OPC UA client write failed.",
            result.Error?.Details));
        PublishFailure(result.Error?.Message ?? "OPC UA client write failed.", result.Error?.Details);
        return result;
    }

    /// <summary>
    /// Обновляет значение тега во встроенном сервере и публикует результат в server snapshot.
    /// </summary>
    public async Task<OpcUaOperationResult> UpdateServerTagAsync(
        OpcUaTagValue value,
        CancellationToken cancellationToken = default)
        => await UpdateServerTagCoreAsync(value, cancellationToken);

    /// <summary>
    /// Отписывается от событий, останавливает runtime и освобождает синхронизационные ресурсы.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _clientStateSubscription.Dispose();
        _serverCommandSubscription.Dispose();
        await StopAsync();
        _gate.Dispose();
    }

    /// <summary>
    /// Запускает одну или обе роли без предварительной остановки уже выключенных ролей.
    /// </summary>
    private async Task StartModeCoreAsync(OpcUaRunMode mode, CancellationToken cancellationToken)
    {
        switch (mode)
        {
            case OpcUaRunMode.None:
                return;
            case OpcUaRunMode.Client:
                await StartClientCoreAsync(cancellationToken);
                return;
            case OpcUaRunMode.Server:
                await StartServerCoreAsync(cancellationToken);
                return;
            case OpcUaRunMode.Both:
                if (CurrentOptions.Server.Enabled)
                {
                    await StartServerCoreAsync(cancellationToken);
                }

                if (CurrentOptions.Client.Enabled)
                {
                    await StartClientCoreAsync(cancellationToken);
                }
                return;
        }
    }

    /// <summary>
    /// Поднимает клиентскую роль и подписывает ее на настроенные telemetry-теги.
    /// </summary>
    private async Task StartClientCoreAsync(CancellationToken cancellationToken)
    {
        if (!CurrentOptions.Client.Enabled)
        {
            PublishClientStatus(OpcUaConnectionStatus.Disconnected, "Client disabled in settings.");
            return;
        }

        var result = await _clientService.ConnectAsync(cancellationToken, CurrentOptions);
        if (!result.Succeeded)
        {
            PublishClientStatus(
                OpcUaConnectionStatus.Faulted,
                result.Error?.Message ?? "Client connection failed.",
                result.Error?.Details);
            return;
        }

        ResetClientSnapshot();
        await SubscribeClientTelemetryAsync(cancellationToken);
    }

    /// <summary>
    /// Останавливает клиентскую роль и освобождает подписки на внешние теги.
    /// </summary>
    private async Task StopClientCoreAsync(CancellationToken cancellationToken)
    {
        foreach (var subscription in _clientSubscriptions)
        {
            subscription.Dispose();
        }

        _clientSubscriptions.Clear();

        var result = await _clientService.DisconnectAsync(cancellationToken);
        if (!result.Succeeded)
        {
            PublishClientStatus(
                OpcUaConnectionStatus.Faulted,
                result.Error?.Message ?? "Client disconnect failed.",
                result.Error?.Details);
        }
        else
        {
            PublishClientStatus(OpcUaConnectionStatus.Disconnected, "Client stopped.");
        }
    }

    /// <summary>
    /// Запускает встроенный сервер, публикует начальные значения и включает demo-telemetry при необходимости.
    /// </summary>
    private async Task StartServerCoreAsync(CancellationToken cancellationToken)
    {
        if (!CurrentOptions.Server.Enabled)
        {
            PublishServerStatus(OpcUaConnectionStatus.Disconnected, "Server disabled in settings.");
            return;
        }

        var result = await _serverService.StartAsync(cancellationToken, CurrentOptions);
        if (!result.Succeeded)
        {
            PublishServerStatus(
                OpcUaConnectionStatus.Faulted,
                result.Error?.Message ?? "Server start failed.",
                result.Error?.Details);
            return;
        }

        ResetServerSnapshot();
        PublishServerStatus(OpcUaConnectionStatus.Connected, $"Server listening at {CurrentOptions.Server.EndpointUrl}.");
        await SeedServerTelemetryAsync(cancellationToken);

        if (CurrentOptions.Server.DemoTelemetryEnabled)
        {
            StartDemoTelemetryLoop(cancellationToken);
        }
    }

    /// <summary>
    /// Останавливает demo-telemetry и сам встроенный OPC UA сервер.
    /// </summary>
    private async Task StopServerCoreAsync(CancellationToken cancellationToken)
    {
        await StopDemoTelemetryLoopAsync();
        var result = await _serverService.StopAsync(cancellationToken);
        if (!result.Succeeded)
        {
            PublishServerStatus(
                OpcUaConnectionStatus.Faulted,
                result.Error?.Message ?? "Server stop failed.",
                result.Error?.Details);
        }
        else
        {
            PublishServerStatus(OpcUaConnectionStatus.Disconnected, "Server stopped.");
        }
    }

    /// <summary>
    /// Останавливает обе роли в порядке клиент, затем сервер.
    /// </summary>
    private async Task StopBothCoreAsync(CancellationToken cancellationToken)
    {
        await StopClientCoreAsync(cancellationToken);
        await StopServerCoreAsync(cancellationToken);
    }

    /// <summary>
    /// Читает стартовые значения клиентских telemetry-тегов и подписывается на дальнейшие изменения.
    /// </summary>
    private async Task SubscribeClientTelemetryAsync(CancellationToken cancellationToken)
    {
        foreach (var subscription in _clientSubscriptions)
        {
            subscription.Dispose();
        }

        _clientSubscriptions.Clear();

        foreach (var address in CurrentOptions.Nodes.ClientTelemetryAddresses())
        {
            var read = await _clientService.ReadTagAsync(address, cancellationToken);
            if (read.Succeeded && read.Value is not null)
            {
                PublishClientTelemetry(read.Value);
            }
            else if (read.Error is not null)
            {
                PublishClientTelemetry(CreateErrorValue(address, read.Error.Message, read.Error.Details));
                PublishFailure(read.Error.Message, read.Error.Details);
            }

            var subscription = await _clientService.SubscribeAsync(
                address,
                PublishClientTelemetry,
                cancellationToken);

            if (subscription.Succeeded && subscription.Value is not null)
            {
                _clientSubscriptions.Add(subscription.Value);
            }
            else if (subscription.Error is not null)
            {
                PublishClientTelemetry(CreateErrorValue(address, subscription.Error.Message, subscription.Error.Details));
                PublishFailure(subscription.Error.Message, subscription.Error.Details);
            }
        }
    }

    /// <summary>
    /// Заполняет серверные telemetry-теги начальными значениями из конфигурации.
    /// </summary>
    private async Task SeedServerTelemetryAsync(CancellationToken cancellationToken)
    {
        _serverCounter = 0;
        foreach (var tag in CurrentOptions.Nodes.ServerTelemetryDefinitions())
        {
            await UpdateServerTagCoreAsync(
                new OpcUaTagValue(tag.Address, CreateInitialValue(tag), DateTimeOffset.Now, "Good"),
                cancellationToken);
        }
    }

    /// <summary>
    /// Запускает фоновую задачу, которая имитирует живое устройство через demo-теги сервера.
    /// </summary>
    private void StartDemoTelemetryLoop(CancellationToken cancellationToken)
    {
        _demoTelemetryCancellation?.Cancel();
        _demoTelemetryCancellation?.Dispose();
        _demoTelemetryCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _demoTelemetryTask = Task.Run(
            () => RunDemoTelemetryAsync(_demoTelemetryCancellation.Token),
            CancellationToken.None);
    }

    /// <summary>
    /// Останавливает фоновую demo-telemetry без зависания UI-команды.
    /// </summary>
    private async Task StopDemoTelemetryLoopAsync()
    {
        _demoTelemetryCancellation?.Cancel();

        if (_demoTelemetryTask is not null)
        {
            try
            {
                await _demoTelemetryTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("OPC UA demo telemetry loop did not stop in time.");
            }
        }

        _demoTelemetryCancellation?.Dispose();
        _demoTelemetryCancellation = null;
        _demoTelemetryTask = null;
    }

    /// <summary>
    /// Периодически изменяет demo-теги сервера, пока runtime не получит отмену.
    /// </summary>
    private async Task RunDemoTelemetryAsync(CancellationToken cancellationToken)
    {
        var interval = Math.Max(250, CurrentOptions.Server.TelemetryUpdateIntervalMilliseconds);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
        var isRunning = true;

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                isRunning = !isRunning;
                _serverCounter++;
                var isRunningAddress = FindTelemetryAddress(CurrentOptions.Nodes.IsRunningIdentifier);
                if (isRunningAddress is not null)
                {
                    await UpdateServerTagCoreAsync(
                        new OpcUaTagValue(isRunningAddress, isRunning, DateTimeOffset.Now, "Good"),
                        cancellationToken);
                }

                var counterAddress = FindTelemetryAddress(CurrentOptions.Nodes.CounterIdentifier);
                if (counterAddress is not null)
                {
                    await UpdateServerTagCoreAsync(
                        new OpcUaTagValue(counterAddress, _serverCounter, DateTimeOffset.Now, "Good"),
                        cancellationToken);
                }

                var messageAddress = FindTelemetryAddress(CurrentOptions.Nodes.ServerMessageIdentifier);
                if (messageAddress is not null)
                {
                    await UpdateServerTagCoreAsync(
                        new OpcUaTagValue(
                            messageAddress,
                            $"Telemetry tick {_serverCounter}",
                            DateTimeOffset.Now,
                            "Good"),
                        cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// Обновляет серверный тег и синхронно отражает результат в runtime snapshot.
    /// </summary>
    private async Task<OpcUaOperationResult> UpdateServerTagCoreAsync(
        OpcUaTagValue value,
        CancellationToken cancellationToken)
    {
        var result = await _serverTagUpdater.UpdateTagValueAsync(value, cancellationToken);
        if (result.Succeeded)
        {
            PublishServerTelemetry(value);
            return result;
        }

        PublishServerTelemetry(CreateErrorValue(
            value.Address,
            result.Error?.Message ?? "OPC UA server update failed.",
            result.Error?.Details));
        PublishFailure(result.Error?.Message ?? "OPC UA server update failed.", result.Error?.Details);
        return result;
    }

    /// <summary>
    /// Создает значение-ошибку, чтобы UI показывал проблему в той же таблице тегов.
    /// </summary>
    private static OpcUaTagValue CreateErrorValue(OpcUaTagAddress address, string message, string? details = null)
        => new(
            address,
            string.IsNullOrWhiteSpace(details) ? message : $"{message} {details}",
            DateTimeOffset.Now,
            "Error");

    /// <summary>
    /// Выполняет lifecycle-команду одной роли под общим gate.
    /// </summary>
    private async Task ExecuteSingleRoleAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await action();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Переводит поток состояний клиента в агрегированный статус runtime.
    /// </summary>
    private void OnClientConnectionStateChanged(OpcUaConnectionState state)
    {
        PublishClientStatus(
            state.Status,
            state.Message ?? state.EndpointUrl ?? state.Status.ToString(),
            state.Status == OpcUaConnectionStatus.Faulted ? state.Message : null);
    }

    /// <summary>
    /// Публикует статус клиента, сохраняя последнее состояние сервера.
    /// </summary>
    private void PublishClientStatus(OpcUaConnectionStatus state, string message, string? error = null)
    {
        OpcUaStatus next;

        lock (_sync)
        {
            next = new OpcUaStatus
            {
                ClientState = state,
                ServerState = _status.ServerState,
                ClientMessage = message,
                ServerMessage = _status.ServerMessage,
                LastError = error ?? _status.LastError,
                UpdatedAt = DateTimeOffset.Now
            };
        }

        PublishStatus(next);
    }

    /// <summary>
    /// Публикует статус сервера, сохраняя последнее состояние клиента.
    /// </summary>
    private void PublishServerStatus(OpcUaConnectionStatus state, string message, string? error = null)
    {
        OpcUaStatus next;

        lock (_sync)
        {
            next = new OpcUaStatus
            {
                ClientState = _status.ClientState,
                ServerState = state,
                ClientMessage = _status.ClientMessage,
                ServerMessage = message,
                LastError = error ?? _status.LastError,
                UpdatedAt = DateTimeOffset.Now
            };
        }

        PublishStatus(next);
    }

    /// <summary>
    /// Сохраняет последнюю ошибку без изменения состояний ролей.
    /// </summary>
    private void PublishFailure(string message, string? details = null)
    {
        var error = string.IsNullOrWhiteSpace(details) ? message : $"{message} {details}";
        OpcUaStatus next;

        lock (_sync)
        {
            next = new OpcUaStatus
            {
                ClientState = _status.ClientState,
                ServerState = _status.ServerState,
                ClientMessage = _status.ClientMessage,
                ServerMessage = _status.ServerMessage,
                LastError = error,
                UpdatedAt = DateTimeOffset.Now
            };
        }

        PublishStatus(next);
    }

    /// <summary>
    /// Атомарно обновляет статус и уведомляет подписчиков runtime.
    /// </summary>
    private void PublishStatus(OpcUaStatus status)
    {
        lock (_sync)
        {
            _status = status;
        }

        StatusChanged?.Invoke(this, status);
    }

    /// <summary>
    /// Обновляет клиентскую telemetry-коллекцию и публикует новый snapshot.
    /// </summary>
    private void PublishClientTelemetry(OpcUaTagValue value)
    {
        OpcUaSnapshot snapshot;

        lock (_sync)
        {
            _clientTelemetry[value.Address.Identifier] = value;
            _clientSnapshot = new OpcUaSnapshot
            {
                Role = OpcUaRuntimeRole.Client,
                TelemetryValues = [.. _clientTelemetry.Values],
                CommandValues = [.. _clientCommands.Values],
                Timestamp = DateTimeOffset.Now
            };
            snapshot = _clientSnapshot;
        }

        SnapshotChanged?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Обновляет клиентские command-значения и публикует новый snapshot.
    /// </summary>
    private void PublishClientCommand(OpcUaTagValue value)
    {
        OpcUaSnapshot snapshot;

        lock (_sync)
        {
            _clientCommands[value.Address.Identifier] = value;
            _clientSnapshot = new OpcUaSnapshot
            {
                Role = OpcUaRuntimeRole.Client,
                TelemetryValues = [.. _clientTelemetry.Values],
                CommandValues = [.. _clientCommands.Values],
                Timestamp = DateTimeOffset.Now
            };
            snapshot = _clientSnapshot;
        }

        SnapshotChanged?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Обновляет серверную telemetry-коллекцию и публикует новый snapshot.
    /// </summary>
    private void PublishServerTelemetry(OpcUaTagValue value)
    {
        OpcUaSnapshot snapshot;

        lock (_sync)
        {
            _serverTelemetry[value.Address.Identifier] = value;
            _serverSnapshot = new OpcUaSnapshot
            {
                Role = OpcUaRuntimeRole.Server,
                TelemetryValues = [.. _serverTelemetry.Values],
                CommandValues = [.. _serverCommands.Values],
                Timestamp = DateTimeOffset.Now
            };
            snapshot = _serverSnapshot;
        }

        SnapshotChanged?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Обновляет command-значения, полученные сервером от внешних клиентов.
    /// </summary>
    private void PublishServerCommand(OpcUaTagValue value)
    {
        OpcUaSnapshot snapshot;

        lock (_sync)
        {
            _serverCommands[value.Address.Identifier] = value;
            _serverSnapshot = new OpcUaSnapshot
            {
                Role = OpcUaRuntimeRole.Server,
                TelemetryValues = [.. _serverTelemetry.Values],
                CommandValues = [.. _serverCommands.Values],
                Timestamp = DateTimeOffset.Now
            };
            snapshot = _serverSnapshot;
        }

        SnapshotChanged?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Очищает клиентский snapshot при новом подключении.
    /// </summary>
    private void ResetClientSnapshot()
    {
        OpcUaSnapshot snapshot;

        lock (_sync)
        {
            _clientTelemetry.Clear();
            _clientCommands.Clear();
            _clientSnapshot = new OpcUaSnapshot
            {
                Role = OpcUaRuntimeRole.Client,
                TelemetryValues = [],
                CommandValues = [],
                Timestamp = DateTimeOffset.Now
            };
            snapshot = _clientSnapshot;
        }

        SnapshotChanged?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Очищает серверный snapshot при новом запуске сервера.
    /// </summary>
    private void ResetServerSnapshot()
    {
        OpcUaSnapshot snapshot;

        lock (_sync)
        {
            _serverTelemetry.Clear();
            _serverCommands.Clear();
            _serverSnapshot = new OpcUaSnapshot
            {
                Role = OpcUaRuntimeRole.Server,
                TelemetryValues = [],
                CommandValues = [],
                Timestamp = DateTimeOffset.Now
            };
            snapshot = _serverSnapshot;
        }

        SnapshotChanged?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Ищет адрес demo-telemetry среди фактически настроенных серверных тегов.
    /// </summary>
    private OpcUaTagAddress? FindTelemetryAddress(string identifier)
        => CurrentOptions.Nodes.ServerTelemetryDefinitions()
            .Select(tag => tag.Address)
            .FirstOrDefault(address => string.Equals(address.Identifier, identifier, StringComparison.Ordinal));

    /// <summary>
    /// Парсит начальное значение тега в поддерживаемый CLR-тип.
    /// </summary>
    private static object CreateInitialValue(OpcUaConfiguredTag tag)
    {
        var text = tag.InitialValue ?? string.Empty;
        return OpcUaDataTypeSupport.TryParse(tag.Address.DataType, text, out var value, out _)
            ? value ?? string.Empty
            : OpcUaDataTypeSupport.DefaultInitialValue(tag.Address.DataType);
    }

    /// <summary>
    /// Адаптирует делегат Action к IObserver для внутренних observable-потоков runtime.
    /// </summary>
    private sealed class ActionObserver<T> : IObserver<T>
    {
        private readonly Action<T> _onNext;

        /// <summary>
        /// Создает observer вокруг делегата обработки нового значения.
        /// </summary>
        public ActionObserver(Action<T> onNext)
        {
            _onNext = onNext;
        }

        /// <summary>
        /// Игнорирует завершение внутреннего observable-потока.
        /// </summary>
        public void OnCompleted()
        {
        }

        /// <summary>
        /// Игнорирует ошибку observable-потока: диагностика публикуется отдельным статусом.
        /// </summary>
        public void OnError(Exception error)
        {
        }

        /// <summary>
        /// Передает новое значение в делегат подписчика.
        /// </summary>
        public void OnNext(T value)
            => _onNext(value);
    }
}
