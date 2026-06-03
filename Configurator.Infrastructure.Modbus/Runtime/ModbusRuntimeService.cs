using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Configurator.Infrastructure.Modbus.Runtime;

/// <summary>
/// Координирует независимый запуск и остановку Modbus клиента и сервера.
/// </summary>
internal sealed class ModbusRuntimeService : IModbusRuntimeService
{
    private readonly IModbusClientService _clientService;
    private readonly IModbusServerService _serverService;
    private readonly IOptionsMonitor<ModbusOptions> _optionsMonitor;
    private readonly ILogger<ModbusRuntimeService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private ModbusStatus _status = ModbusStatus.Stopped;
    private ModbusSnapshot _clientSnapshot = ModbusSnapshot.Empty;
    private ModbusSnapshot _serverSnapshot = ModbusSnapshot.Empty;

    /// <summary>
    /// Создает runtime и подписывает его на события ролей.
    /// </summary>
    public ModbusRuntimeService(
        IModbusClientService clientService,
        IModbusServerService serverService,
        IOptionsMonitor<ModbusOptions> optionsMonitor,
        ILogger<ModbusRuntimeService> logger)
    {
        _clientService = clientService;
        _serverService = serverService;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
        CurrentOptions = _optionsMonitor.CurrentValue.Clone();

        _clientService.StatusChanged += OnClientStatusChanged;
        _clientService.SnapshotChanged += OnSnapshotChanged;
        _serverService.StatusChanged += OnServerStatusChanged;
        _serverService.SnapshotChanged += OnSnapshotChanged;
    }

    /// <summary>
    /// Агрегированный статус клиентской и серверной ролей Modbus.
    /// </summary>
    public ModbusStatus Status
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
    /// Последний snapshot, опубликованный клиентской ролью Modbus.
    /// </summary>
    public ModbusSnapshot ClientSnapshot
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
    /// Последний snapshot, опубликованный серверной ролью Modbus.
    /// </summary>
    public ModbusSnapshot ServerSnapshot
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
    public ModbusOptions CurrentOptions { get; private set; }

    /// <summary>
    /// Уведомляет подписчиков об изменении агрегированного статуса runtime.
    /// </summary>
    public event EventHandler<ModbusStatus>? StatusChanged;

    /// <summary>
    /// Уведомляет подписчиков о новом snapshot клиентской или серверной роли.
    /// </summary>
    public event EventHandler<ModbusSnapshot>? SnapshotChanged;

    /// <summary>
    /// Запускает выбранные роли runtime с актуальными настройками.
    /// </summary>
    public async Task StartAsync(
        ModbusRunMode mode = ModbusRunMode.Both,
        ModbusOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            CurrentOptions = (options ?? _optionsMonitor.CurrentValue).Clone();

            switch (mode)
            {
                case ModbusRunMode.None:
                    await StopBothCoreAsync(cancellationToken);
                    break;
                case ModbusRunMode.Client:
                    await _serverService.StopAsync(cancellationToken);
                    await _clientService.StartAsync(CurrentOptions.Client, cancellationToken);
                    break;
                case ModbusRunMode.Server:
                    await _clientService.StopAsync(cancellationToken);
                    await _serverService.StartAsync(CurrentOptions.Server, cancellationToken);
                    break;
                case ModbusRunMode.Both:
                    if (CurrentOptions.Server.Enabled)
                    {
                        await _serverService.StartAsync(CurrentOptions.Server, cancellationToken);
                    }

                    if (CurrentOptions.Client.Enabled)
                    {
                        await _clientService.StartAsync(CurrentOptions.Client, cancellationToken);
                    }
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Ошибка запуска Modbus runtime");
            PublishStatus(new ModbusStatus
            {
                ClientState = _clientService.State,
                ServerState = _serverService.State,
                ClientMessage = _status.ClientMessage,
                ServerMessage = _status.ServerMessage,
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
        ModbusRunMode mode = ModbusRunMode.Both,
        ModbusOptions? options = null,
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
    public async Task StartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            CurrentOptions = (options ?? _optionsMonitor.CurrentValue).Clone();
            await _clientService.StartAsync(CurrentOptions.Client, cancellationToken);
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
    {
        return ExecuteSingleRoleAsync(() => _clientService.StopAsync(cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Перезапускает клиентскую роль runtime с переданными или текущими настройками.
    /// </summary>
    public async Task RestartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            CurrentOptions = (options ?? _optionsMonitor.CurrentValue).Clone();
            await _clientService.StopAsync(cancellationToken);
            await _clientService.StartAsync(CurrentOptions.Client, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Запускает только серверную роль runtime.
    /// </summary>
    public async Task StartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            CurrentOptions = (options ?? _optionsMonitor.CurrentValue).Clone();
            await _serverService.StartAsync(CurrentOptions.Server, cancellationToken);
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
    {
        return ExecuteSingleRoleAsync(() => _serverService.StopAsync(cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Перезапускает серверную роль runtime с переданными или текущими настройками.
    /// </summary>
    public async Task RestartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            CurrentOptions = (options ?? _optionsMonitor.CurrentValue).Clone();
            await _serverService.StopAsync(cancellationToken);
            await _serverService.StartAsync(CurrentOptions.Server, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Отписывается от событий, останавливает runtime и освобождает синхронизационные ресурсы.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _clientService.StatusChanged -= OnClientStatusChanged;
        _clientService.SnapshotChanged -= OnSnapshotChanged;
        _serverService.StatusChanged -= OnServerStatusChanged;
        _serverService.SnapshotChanged -= OnSnapshotChanged;
        await StopAsync();
        _gate.Dispose();
    }

    private async Task StartModeCoreAsync(ModbusRunMode mode, CancellationToken cancellationToken)
    {
        switch (mode)
        {
            case ModbusRunMode.None:
                return;
            case ModbusRunMode.Client:
                await _clientService.StartAsync(CurrentOptions.Client, cancellationToken);
                return;
            case ModbusRunMode.Server:
                await _serverService.StartAsync(CurrentOptions.Server, cancellationToken);
                return;
            case ModbusRunMode.Both:
                if (CurrentOptions.Server.Enabled)
                {
                    await _serverService.StartAsync(CurrentOptions.Server, cancellationToken);
                }

                if (CurrentOptions.Client.Enabled)
                {
                    await _clientService.StartAsync(CurrentOptions.Client, cancellationToken);
                }
                return;
        }
    }

    private async Task StopBothCoreAsync(CancellationToken cancellationToken)
    {
        await _clientService.StopAsync(cancellationToken);
        await _serverService.StopAsync(cancellationToken);
    }

    private async Task ExecuteSingleRoleAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        // Одиночные команды используют тот же gate, что и Start/Restart,
        // чтобы клиент и сервер не меняли состояние параллельно.
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

    private void OnClientStatusChanged(object? sender, ModbusStatus status)
    {
        ModbusStatus next;

        lock (_sync)
        {
            next = new ModbusStatus
            {
                ClientState = status.ClientState,
                ServerState = _status.ServerState,
                ClientMessage = status.ClientMessage,
                ServerMessage = _status.ServerMessage,
                LastError = status.LastError,
                UpdatedAt = DateTimeOffset.Now
            };
        }

        PublishStatus(next);
    }

    private void OnServerStatusChanged(object? sender, ModbusStatus status)
    {
        ModbusStatus next;

        lock (_sync)
        {
            next = new ModbusStatus
            {
                ClientState = _status.ClientState,
                ServerState = status.ServerState,
                ClientMessage = _status.ClientMessage,
                ServerMessage = status.ServerMessage,
                LastError = status.LastError,
                UpdatedAt = DateTimeOffset.Now
            };
        }

        PublishStatus(next);
    }

    private void OnSnapshotChanged(object? sender, ModbusSnapshot snapshot)
    {
        lock (_sync)
        {
            // Храним отдельные snapshots ролей: фасад и UI могут читать их независимо,
            // особенно когда одновременно запущены клиент и сервер.
            if (snapshot.Role == ModbusRuntimeRole.Client)
            {
                _clientSnapshot = snapshot;
            }
            else if (snapshot.Role == ModbusRuntimeRole.Server)
            {
                _serverSnapshot = snapshot;
            }
        }

        SnapshotChanged?.Invoke(this, snapshot);
    }

    private void PublishStatus(ModbusStatus status)
    {
        lock (_sync)
        {
            _status = status;
        }

        StatusChanged?.Invoke(this, status);
    }
}
