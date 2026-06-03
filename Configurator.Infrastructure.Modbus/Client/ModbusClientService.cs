using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using CP.IO.Ports;
using Microsoft.Extensions.Logging;
using ModbusRx;
using ModbusRx.Device;
using System.Runtime.ExceptionServices;

namespace Configurator.Infrastructure.Modbus.Client;

/// <summary>
/// Modbus TCP клиент: читает карту данных и пишет значения по командам UI.
/// </summary>
internal sealed class ModbusClientService : IModbusClientService
{
    private readonly ILogger<ModbusClientService> _logger;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private CancellationTokenSource? _loopCancellation;
    private Task? _loopTask;
    private ModbusIpMaster? _master;
    private ModbusClientDataReader? _reader;
    private ModbusEndpointOptions _options = new();
    private string? _lastPollError;
    private ModbusConnectionState _state = ModbusConnectionState.Stopped;
    private ModbusSnapshot _snapshot = ModbusSnapshot.Empty;

    /// <summary>
    /// Создает клиентский сервис.
    /// </summary>
    public ModbusClientService(ILogger<ModbusClientService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Текущее состояние клиентского подключения Modbus TCP.
    /// </summary>
    public ModbusConnectionState State => _state;

    /// <summary>
    /// Последний snapshot значений, прочитанных клиентской ролью.
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
            if (_state is ModbusConnectionState.Running or ModbusConnectionState.Starting or ModbusConnectionState.Reconnecting)
            {
                return;
            }

            _options = NormalizeOptions(options);
            PublishStatus(ModbusConnectionState.Starting, "Клиент подключается");

            _loopCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loopTask = Task.Run(
                () => ConnectAndPollAsync(_loopCancellation.Token),
                CancellationToken.None);
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
            await StopCoreAsync();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Записывает одно coil-значение в подключенное Modbus-устройство.
    /// </summary>
    public async Task WriteCoilAsync(int address, bool value, CancellationToken cancellationToken = default)
    {
        if (!_options.CoilsEnabled)
        {
            throw new InvalidOperationException("Coils клиента отключены в настройках.");
        }

        ValidateAddress(address, _options.CoilCount, "Coil");

        await ExecuteWriteAsync(
            reader => reader.WriteCoilAsync(_options.CoilStartAddress + address, value),
            $"Клиент записал Coil[{address}]={value}",
            "Coils",
            cancellationToken);
    }

    /// <summary>
    /// Записывает один holding-регистр в подключенное Modbus-устройство.
    /// </summary>
    public async Task WriteRegisterAsync(int address, ushort value, CancellationToken cancellationToken = default)
    {
        if (!_options.HoldingRegistersEnabled)
        {
            throw new InvalidOperationException("Holding Registers клиента отключены в настройках.");
        }

        ValidateAddress(address, _options.RegisterCount, "Register");

        await ExecuteWriteAsync(
            reader => reader.WriteRegisterAsync(_options.HoldingRegisterStartAddress + address, value),
            $"Клиент записал Register[{address}]={value}",
            "Holding Registers",
            cancellationToken);
    }

    /// <summary>
    /// Записывает последовательность holding-регистров одним Modbus-запросом.
    /// </summary>
    public async Task WriteRegistersAsync(int startAddress, ushort[] values, CancellationToken cancellationToken = default)
    {
        if (!_options.HoldingRegistersEnabled)
        {
            throw new InvalidOperationException("Holding Registers клиента отключены в настройках.");
        }

        ValidateRange(startAddress, values.Length, _options.RegisterCount, "Register");

        await ExecuteWriteAsync(
            reader => reader.WriteRegistersAsync(_options.HoldingRegisterStartAddress + startAddress, values),
            $"Клиент записал Registers[{startAddress}..{startAddress + values.Length - 1}]",
            "Holding Registers",
            cancellationToken);
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

    private async Task ConnectAndPollAsync(CancellationToken cancellationToken)
    {
        // Цикл держит клиент живым: после сетевой ошибки соединение закрывается,
        // статус переводится в Reconnecting, затем попытка подключения повторяется.
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectCoreAsync(cancellationToken);

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await ReadAndPublishAsync(cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            await ResetConnectionAsync(CancellationToken.None);
                            break;
                        }

                        _logger.LogWarning(ex, "Ошибка опроса Modbus клиента");
                        PublishStatus(ModbusConnectionState.Reconnecting, "Соединение потеряно, клиент переподключается", ex.Message);
                        await ResetConnectionAsync(cancellationToken);
                        break;
                    }

                    await Task.Delay(Math.Max(100, _options.PollIntervalMs), cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                _logger.LogWarning(ex, "Не удалось подключиться к Modbus серверу");
                PublishStatus(ModbusConnectionState.Reconnecting, "Сервер недоступен, клиент ожидает подключения", ex.Message);
                await ResetConnectionAsync(CancellationToken.None);
                await DelayBeforeRetryAsync(cancellationToken);
            }
        }
    }

    private async Task ConnectCoreAsync(CancellationToken cancellationToken)
    {
        await _ioGate.WaitAsync(cancellationToken);

        try
        {
            DisposeConnectionCore();

            var tcpClient = new TcpClientRx(_options.Host, _options.Port);
            _master = ModbusIpMaster.CreateIp(tcpClient);

            if (_master.Transport is not null)
            {
                var timeout = Math.Max(1000, _options.PollIntervalMs * 2);
                _master.Transport.ReadTimeout = timeout;
                _master.Transport.WriteTimeout = timeout;
            }

            _reader = new ModbusClientDataReader(_master, _options.UnitId);

            if (cancellationToken.IsCancellationRequested)
            {
                DisposeConnectionCore();
                cancellationToken.ThrowIfCancellationRequested();
            }

            PublishStatus(
                ModbusConnectionState.Running,
                $"Клиент подключен к {_options.Host}:{_options.Port}, UnitId={_options.UnitId}");
        }
        finally
        {
            _ioGate.Release();
        }
    }

    private async Task ExecuteWriteAsync(
        Func<ModbusClientDataReader, Task> write,
        string successMessage,
        string failureArea,
        CancellationToken cancellationToken)
    {
        var written = false;
        var reconnect = false;
        Exception? failure = null;
        var failureMessage = string.Empty;
        // Запись и polling используют один master, поэтому проходят через общий I/O gate.
        await _ioGate.WaitAsync(cancellationToken);

        try
        {
            var reader = EnsureReader();
            await write(reader);
            written = true;
            PublishStatus(ModbusConnectionState.Running, successMessage);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Ошибка записи Modbus клиента");
            failure = ex;
            failureMessage = BuildAreaError(failureArea, ex);
            reconnect = _state == ModbusConnectionState.Running && !IsModbusApplicationException(ex);
            var state = reconnect ? ModbusConnectionState.Reconnecting : _state;
            var message = reconnect
                ? "Запись недоступна: клиент переподключается"
                : failureMessage;
            PublishStatus(state, message, failureMessage);
        }
        finally
        {
            _ioGate.Release();
        }

        if (reconnect)
        {
            await ResetConnectionAsync(cancellationToken);
        }

        if (failure is not null)
        {
            throw new InvalidOperationException(failureMessage, failure);
        }

        if (written)
        {
            await ReadAndPublishAsync(cancellationToken);
        }
    }

    private async Task ReadAndPublishAsync(CancellationToken cancellationToken)
    {
        await _ioGate.WaitAsync(cancellationToken);

        try
        {
            var reader = EnsureReader();
            // Если одна область недоступна из-за настроек внешнего устройства, сохраняем
            // последнюю успешную область и не роняем клиент, пока SDK сообщает Modbus exception.
            var coils = _snapshot.Role == ModbusRuntimeRole.Client
                ? _snapshot.Coils.ToArray()
                : Array.Empty<bool>();
            var registers = _snapshot.Role == ModbusRuntimeRole.Client
                ? _snapshot.HoldingRegisters.ToArray()
                : Array.Empty<ushort>();
            var attempted = false;
            var succeeded = false;
            Exception? firstFailure = null;
            string? shortError = null;

            if (_options.CoilsEnabled && _options.CoilCount > 0)
            {
                attempted = true;

                try
                {
                    coils = await reader.ReadCoilsAsync(_options.CoilStartAddress, _options.CoilCount);
                    succeeded = true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Ошибка чтения Coils Modbus клиента");
                    firstFailure ??= ex;
                    shortError = BuildAreaError("Coils", ex);
                }
            }
            else
            {
                coils = Array.Empty<bool>();
            }

            if (_options.HoldingRegistersEnabled && _options.RegisterCount > 0)
            {
                attempted = true;

                try
                {
                    registers = await reader.ReadHoldingRegistersAsync(
                        _options.HoldingRegisterStartAddress,
                        _options.RegisterCount);
                    succeeded = true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Ошибка чтения Holding Registers Modbus клиента");
                    firstFailure ??= ex;
                    shortError ??= BuildAreaError("Holding Registers", ex);
                }
            }
            else
            {
                registers = Array.Empty<ushort>();
            }

            if (attempted && !succeeded && firstFailure is not null)
            {
                if (!IsModbusApplicationException(firstFailure))
                {
                    ExceptionDispatchInfo.Capture(firstFailure).Throw();
                }

                shortError ??= BuildAreaError("Modbus", firstFailure);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = new ModbusSnapshot
            {
                Role = ModbusRuntimeRole.Client,
                Coils = coils,
                HoldingRegisters = registers,
                DecodedRegisters = ModbusRegistersCodec.Decode(registers),
                Timestamp = DateTimeOffset.Now
            };

            _snapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);

            if (!string.IsNullOrWhiteSpace(shortError))
            {
                _lastPollError = shortError;
                PublishStatus(ModbusConnectionState.Running, "Клиент подключен, часть карты недоступна", shortError);
            }
            else if (!string.IsNullOrWhiteSpace(_lastPollError))
            {
                _lastPollError = null;
                PublishStatus(
                    ModbusConnectionState.Running,
                    $"Клиент подключен к {_options.Host}:{_options.Port}, UnitId={_options.UnitId}");
            }
            else if (_state != ModbusConnectionState.Running)
            {
                PublishStatus(
                    ModbusConnectionState.Running,
                    $"Клиент подключен к {_options.Host}:{_options.Port}, UnitId={_options.UnitId}");
            }
        }
        finally
        {
            _ioGate.Release();
        }
    }

    private async Task StopCoreAsync()
    {
        if (_state == ModbusConnectionState.Stopped
            && _loopTask is null
            && _master is null
            && _reader is null)
        {
            return;
        }

        _loopCancellation?.Cancel();

        var loopCompleted = true;

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
                loopCompleted = false;
                _logger.LogWarning("Клиент Modbus не успел остановить фоновую задачу за отведённое время");
            }
        }

        _loopCancellation?.Dispose();
        _loopCancellation = null;
        _loopTask = null;
        if (loopCompleted)
        {
            await ResetConnectionAsync(CancellationToken.None);
        }
        else if (_ioGate.Wait(0))
        {
            try
            {
                DisposeConnectionCore();
            }
            finally
            {
                _ioGate.Release();
            }
        }

        PublishStatus(ModbusConnectionState.Stopped, "Клиент остановлен");
    }

    private ModbusClientDataReader EnsureReader()
    {
        return _reader ?? throw new InvalidOperationException("Клиент Modbus не запущен.");
    }

    private void PublishStatus(ModbusConnectionState state, string message, string? error = null)
    {
        _state = state;
        StatusChanged?.Invoke(this, new ModbusStatus
        {
            ClientState = state,
            ClientMessage = message,
            LastError = error,
            UpdatedAt = DateTimeOffset.Now
        });
    }

    private async Task ResetConnectionAsync(CancellationToken cancellationToken)
    {
        await _ioGate.WaitAsync(cancellationToken);

        try
        {
            DisposeConnectionCore();
        }
        finally
        {
            _ioGate.Release();
        }
    }

    private void DisposeConnectionCore()
    {
        _reader = null;
        _lastPollError = null;
        _master?.Dispose();
        _master = null;
    }

    private async Task DelayBeforeRetryAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Math.Max(250, _options.PollIntervalMs), cancellationToken);
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
        clone.Host = string.IsNullOrWhiteSpace(clone.Host) ? "127.0.0.1" : clone.Host.Trim();
        return clone;
    }

    private static bool IsModbusApplicationException(Exception exception)
    {
        return exception is SlaveException
            or ArgumentOutOfRangeException
            or InvalidOperationException;
    }

    private static string BuildAreaError(string area, Exception exception)
    {
        if (exception is SlaveException slaveException)
        {
            return slaveException.SlaveExceptionCode == 2
                ? BuildIllegalAddressError(area)
                : $"{area}: Modbus exception {slaveException.SlaveExceptionCode}.";
        }

        return $"{area}: {TrimForUi(exception.Message)}";
    }

    private static string BuildIllegalAddressError(string area)
    {
        return area == "Coils"
            ? "Coils: Illegal Data Address, проверьте CoilCount/StartAddress в CODESYS."
            : "Holding Registers: Illegal Data Address, проверьте RegisterCount/StartAddress в CODESYS.";
    }

    private static string TrimForUi(string message)
    {
        const int maxLength = 180;

        if (string.IsNullOrWhiteSpace(message))
        {
            return "ошибка Modbus.";
        }

        var singleLine = message.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= maxLength
            ? singleLine
            : string.Concat(singleLine.AsSpan(0, maxLength), "...");
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
