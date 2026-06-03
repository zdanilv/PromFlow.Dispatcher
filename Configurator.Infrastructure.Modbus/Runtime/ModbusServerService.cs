using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Microsoft.Extensions.Logging;
using ModbusRx.Data;
using ModbusRx.Device;

namespace Configurator.Infrastructure.Modbus.Runtime;

/// <summary>
/// Встроенный Modbus TCP сервер с ручным управлением локальной картой данных.
/// </summary>
internal sealed class ModbusServerService : IModbusServerService
{
    private readonly ILogger<ModbusServerService> _logger;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private CancellationTokenSource? _loopCancellation;
    private Task? _loopTask;
    private ModbusServer? _server;
    private IDisposable? _tcpSubscription;
    private ModbusEndpointOptions _options = new();
    private ModbusConnectionState _state = ModbusConnectionState.Stopped;
    private ModbusSnapshot _snapshot = ModbusSnapshot.Empty;

    /// <summary>
    /// Создает серверный сервис.
    /// </summary>
    public ModbusServerService(ILogger<ModbusServerService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Текущее состояние встроенного Modbus TCP сервера.
    /// </summary>
    public ModbusConnectionState State => _state;

    /// <summary>
    /// Последний snapshot значений, опубликованных серверной ролью.
    /// </summary>
    public ModbusSnapshot Snapshot => _snapshot;

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
    public async Task StartAsync(ModbusEndpointOptions options, CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);

        try
        {
            if (!options.Enabled)
            {
                PublishStatus(ModbusConnectionState.Stopped, "Сервер выключен в настройках");
                return;
            }

            if (_state is ModbusConnectionState.Running or ModbusConnectionState.Starting)
            {
                return;
            }

            _options = NormalizeOptions(options);
            PublishStatus(ModbusConnectionState.Starting, "Сервер запускается");

            try
            {
                // DataStore в ModbusRx адресуется с единицы, поэтому размер создается на один
                // больше, а наружный API сервиса остается нулевым для UI и data map.
                _server = new ModbusServer
                {
                    DataStore = DataStoreFactory.CreateDefaultDataStore(
                        coilsCount: checked((ushort)(_options.CoilCount + 1)),
                        inputsCount: 1,
                        holdingRegistersCount: checked((ushort)(_options.RegisterCount + 1)),
                        inputRegistersCount: 1)
                };
                _tcpSubscription = _server.StartTcpServer(_options.Port, (byte)_options.UnitId);
                _server.Start();

                _loopCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _loopTask = Task.Run(
                    () => PublishSnapshotsAsync(_loopCancellation.Token),
                    CancellationToken.None);

                PublishStatus(
                    ModbusConnectionState.Running,
                    $"Сервер слушает {_options.BindAddress}:{_options.Port}, UnitId={_options.UnitId}");
                await PublishSnapshotAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Не удалось запустить Modbus сервер");
                await StopCoreAsync();
                PublishStatus(ModbusConnectionState.Faulted, "Ошибка запуска сервера", ex.Message);
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
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);

        try
        {
            if (_state == ModbusConnectionState.Stopped
                && _loopTask is null
                && _server is null)
            {
                return;
            }

            await StopCoreAsync();
            PublishStatus(ModbusConnectionState.Stopped, "Сервер остановлен");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Обновляет coil-значение в локальной карте встроенного сервера.
    /// </summary>
    public async Task SetCoilAsync(int address, bool value, CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken);

        try
        {
            var server = EnsureServer();
            ValidateAddress(address, _options.CoilCount, "Coil");
            server.DataStore!.CoilDiscretes[address + 1] = value;
            PublishStatus(ModbusConnectionState.Running, $"Сервер установил Coil[{address}]={value}");
        }
        finally
        {
            _ioGate.Release();
        }

        await PublishSnapshotAsync(cancellationToken);
    }

    /// <summary>
    /// Обновляет один holding-регистр в локальной карте встроенного сервера.
    /// </summary>
    public async Task SetRegisterAsync(int address, ushort value, CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken);

        try
        {
            var server = EnsureServer();
            ValidateAddress(address, _options.RegisterCount, "Register");
            server.DataStore!.HoldingRegisters[address + 1] = value;
            PublishStatus(ModbusConnectionState.Running, $"Сервер установил Register[{address}]={value}");
        }
        finally
        {
            _ioGate.Release();
        }

        await PublishSnapshotAsync(cancellationToken);
    }

    /// <summary>
    /// Обновляет блок holding-регистров в локальной карте встроенного сервера.
    /// </summary>
    public async Task SetRegistersAsync(int startAddress, ushort[] values, CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken);

        try
        {
            var server = EnsureServer();
            ValidateRange(startAddress, values.Length, _options.RegisterCount, "Register");

            for (var index = 0; index < values.Length; index++)
            {
                server.DataStore!.HoldingRegisters[startAddress + index + 1] = values[index];
            }

            PublishStatus(
                ModbusConnectionState.Running,
                $"Сервер установил Registers[{startAddress}..{startAddress + values.Length - 1}]");
        }
        finally
        {
            _ioGate.Release();
        }

        await PublishSnapshotAsync(cancellationToken);
    }

    /// <summary>
    /// Отписывается от событий, останавливает runtime и освобождает синхронизационные ресурсы.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifecycleGate.Dispose();
        _ioGate.Dispose();
    }

    private async Task PublishSnapshotsAsync(CancellationToken cancellationToken)
    {
        // Сервер тоже публикует snapshots по таймеру: так UI видит изменения,
        // внесенные внешним Modbus TCP клиентом напрямую в локальную карту.
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PublishSnapshotAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Ошибка обновления snapshot Modbus сервера");
                PublishStatus(ModbusConnectionState.Faulted, "Ошибка чтения карты сервера", ex.Message);
            }

            await Task.Delay(Math.Max(100, _options.PollIntervalMs), cancellationToken);
        }
    }

    private async Task PublishSnapshotAsync(CancellationToken cancellationToken)
    {
        await _ioGate.WaitAsync(cancellationToken);

        try
        {
            var server = EnsureServer();
            var dataStore = server.DataStore
                ?? throw new InvalidOperationException("Карта данных сервера не инициализирована.");
            var coils = new bool[_options.CoilCount];
            var registers = new ushort[_options.RegisterCount];

            for (var address = 0; address < coils.Length; address++)
            {
                coils[address] = dataStore.CoilDiscretes[address + 1];
            }

            for (var address = 0; address < registers.Length; address++)
            {
                registers[address] = dataStore.HoldingRegisters[address + 1];
            }

            var snapshot = new ModbusSnapshot
            {
                Role = ModbusRuntimeRole.Server,
                Coils = coils,
                HoldingRegisters = registers,
                DecodedRegisters = ModbusRegistersCodec.Decode(registers),
                Timestamp = DateTimeOffset.Now
            };

            _snapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    private async Task StopCoreAsync()
    {
        _loopCancellation?.Cancel();

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Сервер Modbus не успел остановить фоновую задачу за отведённое время");
            }
        }

        _loopCancellation?.Dispose();
        _loopCancellation = null;
        _loopTask = null;

        _server?.Stop();
        _tcpSubscription?.Dispose();
        _tcpSubscription = null;
        _server?.Dispose();
        _server = null;
        _state = ModbusConnectionState.Stopped;
    }

    private ModbusServer EnsureServer()
    {
        return _server ?? throw new InvalidOperationException("Сервер Modbus не запущен.");
    }

    private void PublishStatus(ModbusConnectionState state, string message, string? error = null)
    {
        _state = state;
        StatusChanged?.Invoke(this, new ModbusStatus
        {
            ServerState = state,
            ServerMessage = message,
            LastError = error,
            UpdatedAt = DateTimeOffset.Now
        });
    }

    private static ModbusEndpointOptions NormalizeOptions(ModbusEndpointOptions options)
    {
        var clone = options.Clone();
        clone.Port = Math.Clamp(clone.Port, 1, 65535);
        clone.UnitId = Math.Clamp(clone.UnitId, 1, 247);
        clone.PollIntervalMs = Math.Clamp(clone.PollIntervalMs, 100, 60000);
        clone.CoilStartAddress = Math.Clamp(clone.CoilStartAddress, 0, ushort.MaxValue);
        clone.HoldingRegisterStartAddress = Math.Clamp(clone.HoldingRegisterStartAddress, 0, ushort.MaxValue);
        clone.CoilCount = Math.Clamp(clone.CoilCount, 0, 2000);
        clone.RegisterCount = Math.Clamp(clone.RegisterCount, 0, 123);
        clone.BindAddress = string.IsNullOrWhiteSpace(clone.BindAddress) ? "127.0.0.1" : clone.BindAddress.Trim();
        return clone;
    }

    private static void ValidateAddress(int address, int count, string name)
    {
        if (address < 0 || address >= count)
        {
            throw new ArgumentOutOfRangeException(nameof(address), $"{name} address {address} вне диапазона 0..{count - 1}.");
        }
    }

    private static void ValidateRange(int startAddress, int length, int count, string name)
    {
        if (length == 0)
        {
            return;
        }

        if (startAddress < 0 || startAddress + length > count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startAddress),
                $"{name} range {startAddress}..{startAddress + length - 1} вне диапазона 0..{count - 1}.");
        }
    }
}
