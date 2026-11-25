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
internal sealed class ModbusServerService : IModbusServerService, IModbusBitPulseProvider, IModbusObservedWriter
{
    private readonly ILogger<ModbusServerService> _logger;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly ModbusPriorityGate _ioGate = new();
    private CancellationTokenSource? _loopCancellation;
    private Task? _loopTask;
    private ModbusServer? _server;
    private IDisposable? _tcpSubscription;
    private ModbusEndpointOptions _options = new();
    private ModbusConnectionState _state = ModbusConnectionState.Stopped;
    private ModbusSnapshot _snapshot = ModbusSnapshot.Empty;
    private Guid _sourceSessionId;

    private static bool ReadObservedBit(DataStore store, ModbusWriteAddress address) =>
        address.Area == ModbusDataArea.Coil
            ? store.CoilDiscretes[address.Offset + 1]
            : (store.HoldingRegisters[address.Offset + 1] & (1 << address.BitIndex!.Value)) != 0;

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

    IModbusBitPulseTarget IModbusBitPulseProvider.CaptureBitPulseTarget(ModbusBitAddressOptions address)
    {
        var options = _options;
        var server = _server;
        var dataStore = server?.DataStore;
        if (_state != ModbusConnectionState.Running || server is null || dataStore is null)
            throw new ModbusBitPulseNotSentException("The original Modbus server ACK target is unavailable.");

        try
        {
            if (address.Area == ModbusDataArea.Coil && address.BitIndex is null)
                ValidateAddress(address.Address, options.CoilCount, "Coil");
            else if (address.Area == ModbusDataArea.HoldingRegister && address.BitIndex is >= 0 and <= 15)
                ValidateAddress(address.Address, options.RegisterCount, "Holding Register");
            else
                throw new ArgumentException("The server ACK bit address is invalid.", nameof(address));
        }
        catch (ArgumentException ex)
        {
            throw new ModbusBitPulseNotSentException("Invalid Modbus server ACK target.", ex);
        }

        var area = address.Area;
        var offset = address.Address;
        var bitIndex = address.BitIndex;
        return new ModbusBitPulseTarget(
            $"Server {options.BindAddress}:{options.Port}, UnitId={options.UnitId}, {area}[{offset}], bit={bitIndex}",
            (value, token) => WriteBitPulseEdgeAsync(options, server, dataStore, area, offset, bitIndex, value, token));
    }

    private async Task WriteBitPulseEdgeAsync(
        ModbusEndpointOptions options,
        ModbusServer server,
        DataStore dataStore,
        ModbusDataArea area,
        int offset,
        int? bitIndex,
        bool value,
        CancellationToken cancellationToken)
    {
        try
        {
            await _ioGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            throw new ModbusBitPulseNotSentException("The ACK edge was cancelled before acquiring server I/O.", ex);
        }

        var writeStarted = false;
        var syncTaken = false;
        var writeLockTaken = false;
        try
        {
            if (_state != ModbusConnectionState.Running || !ReferenceEquals(options, _options)
                || !ReferenceEquals(server, _server) || !ReferenceEquals(dataStore, server.DataStore))
                throw new ModbusBitPulseNotSentException("The original Modbus server ACK session is no longer available.");

            // Standard ModbusRx requests use SyncRoot, optimized requests use Lock.
            // Cover both while reading and updating the live word, with bounded waits.
            while (!syncTaken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Monitor.TryEnter(dataStore.SyncRoot, 25, ref syncTaken);
            }
            while (!writeLockTaken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writeLockTaken = dataStore.Lock.TryEnterWriteLock(25);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (area == ModbusDataArea.HoldingRegister)
            {
                var word = dataStore.HoldingRegisters[offset + 1];
                var mask = checked((ushort)(1 << bitIndex!.Value));
                writeStarted = true;
                dataStore.HoldingRegisters[offset + 1] = value ? (ushort)(word | mask) : (ushort)(word & ~mask);
            }
            else
            {
                writeStarted = true;
                dataStore.CoilDiscretes[offset + 1] = value;
            }
        }
        catch (ModbusBitPulseNotSentException)
        {
            throw;
        }
        catch (Exception ex) when (!writeStarted)
        {
            throw new ModbusBitPulseNotSentException("The server ACK edge was not written; its live word or preparation is unavailable.", ex);
        }
        finally
        {
            if (writeLockTaken)
                dataStore.Lock.ExitWriteLock();
            if (syncTaken)
                Monitor.Exit(dataStore.SyncRoot);
            _ioGate.Release();
        }
    }

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
            _sourceSessionId = Guid.NewGuid();
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
    public Task SetCoilAsync(int address, bool value, CancellationToken cancellationToken = default)
        => SetCoilAsync(address, value, cancellationToken, null);

    private async Task SetCoilAsync(int address, bool value, CancellationToken cancellationToken,
        Action<ModbusWriteReceipt>? observe)
    {
        await _ioGate.WaitAsync(cancellationToken);

        try
        {
            var server = EnsureServer();
            ValidateAddress(address, _options.CoilCount, "Coil");
            observe?.Invoke(new(ModbusRuntimeRole.Server, _sourceSessionId, ModbusObservationSequence.Next()));
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
    public Task SetRegisterAsync(int address, ushort value, CancellationToken cancellationToken = default)
        => SetRegisterAsync(address, value, cancellationToken, null);

    private async Task SetRegisterAsync(int address, ushort value, CancellationToken cancellationToken,
        Action<ModbusWriteReceipt>? observe)
    {
        await _ioGate.WaitAsync(cancellationToken);

        try
        {
            var server = EnsureServer();
            ValidateAddress(address, _options.RegisterCount, "Register");
            observe?.Invoke(new(ModbusRuntimeRole.Server, _sourceSessionId, ModbusObservationSequence.Next()));
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
    public Task SetRegistersAsync(int startAddress, ushort[] values, CancellationToken cancellationToken = default)
        => SetRegistersAsync(startAddress, values, cancellationToken, null);

    private async Task SetRegistersAsync(int startAddress, ushort[] values, CancellationToken cancellationToken,
        Action<ModbusWriteReceipt>? observe)
    {
        await _ioGate.WaitAsync(cancellationToken);

        try
        {
            var server = EnsureServer();
            ValidateRange(startAddress, values.Length, _options.RegisterCount, "Register");
            observe?.Invoke(new(ModbusRuntimeRole.Server, _sourceSessionId, ModbusObservationSequence.Next()));

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

    async Task<ModbusWriteReceipt> IModbusObservedWriter.WriteObservedAsync(
        ModbusWriteAddress target, ModbusWriteAddress? feedback, bool priority, bool pulse,
        Action<Guid> validateBeforeRead, Func<ModbusWriteReceipt, bool, ModbusWritePayload> prepare,
        Action<ModbusWritePayload> committed, CancellationToken cancellationToken)
    {
        await _ioGate.WaitAsync(cancellationToken, priority).ConfigureAwait(false);
        try
        {
            await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            validateBeforeRead(_sourceSessionId);
            var dataStore = EnsureServer().DataStore!;
            var receipt = new ModbusWriteReceipt(ModbusRuntimeRole.Server, _sourceSessionId, ModbusObservationSequence.Next());
            var syncTaken = false;
            var writeLockTaken = false;
            ModbusWritePayload payload;
            try
            {
                while (!syncTaken)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Monitor.TryEnter(dataStore.SyncRoot, 25, ref syncTaken);
                }

                while (!writeLockTaken)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    writeLockTaken = dataStore.Lock.TryEnterWriteLock(25);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var alreadyHigh = feedback is { } response &&
                    (ReadObservedBit(dataStore, target) || ReadObservedBit(dataStore, response));
                payload = prepare(receipt, alreadyHigh);
                var (area, address, _) = target;
                if (area == ModbusDataArea.Coil)
                {
                    ValidateAddress(address, _options.CoilCount, "Coil");
                    dataStore.CoilDiscretes[address + 1] = payload.Coil;
                }
                else
                {
                    ValidateRange(address, payload.Registers.Length, _options.RegisterCount, "Register");
                    if (payload.BitIndex is { } bitIndex)
                    {
                        var current = dataStore.HoldingRegisters[address + 1];
                        var mask = 1 << bitIndex;
                        payload.Registers[0] = (ushort)(payload.BitValue ? current | mask : current & ~mask);
                    }

                    for (var index = 0; index < payload.Registers.Length; index++)
                        dataStore.HoldingRegisters[address + index + 1] = payload.Registers[index];
                }
            }
            finally
            {
                if (writeLockTaken)
                    dataStore.Lock.ExitWriteLock();
                if (syncTaken)
                    Monitor.Exit(dataStore.SyncRoot);
            }

            committed(payload);
            var message = target.Area == ModbusDataArea.Coil
                ? $"Сервер установил Coil[{target.Offset}]={payload.Coil}"
                : payload.Registers.Length == 1
                    ? $"Сервер установил Register[{target.Offset}]={payload.Registers[0]}"
                    : $"Сервер установил Registers[{target.Offset}..{target.Offset + payload.Registers.Length - 1}]";
            PublishStatus(ModbusConnectionState.Running, message);
            PublishSnapshotCore();
            return receipt;
        }
        finally
        {
            _ioGate.Release();
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
            PublishSnapshotCore();
        }
        finally
        {
            _ioGate.Release();
        }
    }

    private void PublishSnapshotCore()
    {
        var server = EnsureServer();
        var dataStore = server.DataStore
            ?? throw new InvalidOperationException("Карта данных сервера не инициализирована.");
        var coils = new bool[_options.CoilCount];
        var registers = new ushort[_options.RegisterCount];
        var coilsSequence = ModbusObservationSequence.Next();

        for (var address = 0; address < coils.Length; address++)
        {
            coils[address] = dataStore.CoilDiscretes[address + 1];
        }

        for (var address = 0; address < registers.Length; address++)
        {
            registers[address] = dataStore.HoldingRegisters[address + 1];
        }

        var readAt = DateTimeOffset.Now;

        var snapshot = new ModbusSnapshot
        {
            Role = ModbusRuntimeRole.Server,
            SourceSessionId = _sourceSessionId,
            ReadSequence = coilsSequence,
            CoilsReadState = coils.Length > 0
                ? new(true, true, true, readAt, coilsSequence)
                : ModbusAreaReadState.Unavailable(false),
            HoldingRegistersReadState = registers.Length > 0
                ? new(true, true, true, readAt, coilsSequence)
                : ModbusAreaReadState.Unavailable(false),
            Coils = coils,
            HoldingRegisters = registers,
            DecodedRegisters = ModbusRegistersCodec.Decode(registers),
            Timestamp = readAt
        };

        _snapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
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

        // ACK target validation and datastore access share this gate with disposal.
        await _ioGate.WaitAsync();
        try
        {
            _server?.Stop();
            _tcpSubscription?.Dispose();
            _tcpSubscription = null;
            _server?.Dispose();
            _server = null;
            _state = ModbusConnectionState.Stopped;
        }
        finally
        {
            _ioGate.Release();
        }
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
