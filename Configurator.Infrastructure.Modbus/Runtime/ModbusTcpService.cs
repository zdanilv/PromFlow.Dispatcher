using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Infrastructure.Modbus.Archiving;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Configurator.Infrastructure.Modbus.Runtime;

/// <summary>
/// Безопасный высокоуровневый фасад поверх существующих сервисов Modbus клиента и сервера.
/// </summary>
internal sealed class ModbusTcpService : IModbusTcpService, IModbusDataSnapshotSource, IModbusDataMapRuntime
{
    private readonly IModbusRuntimeService _runtimeService;
    private readonly IModbusClientService _clientService;
    private readonly IModbusServerService _serverService;
    private readonly IOptionsMonitor<ModbusOptions> _optionsMonitor;
    private readonly IModbusDataMapValidator _dataMapValidator;
    private readonly ModbusPhysicalWriteAuditSink _physicalWriteAuditSink;
    private readonly ILogger<ModbusTcpService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private readonly Dictionary<string, ModbusDataPointOptions> _dataMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModbusDataValue> _latestValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Action<ModbusDataValue>>> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, ushort> _holdingRegisterShadow = [];
    private int _disposed;
    private ModbusOptions _currentOptions;
    private ModbusServiceState _state = ModbusServiceState.Stopped;
    private ModbusDataSnapshot _currentDataSnapshot = ModbusDataSnapshot.Empty;

    /// <summary>
    /// Создает фасад и подписывает его на связанный Modbus runtime.
    /// </summary>
    public ModbusTcpService(
        IModbusRuntimeService runtimeService,
        IModbusClientService clientService,
        IModbusServerService serverService,
        IOptionsMonitor<ModbusOptions> optionsMonitor,
        IModbusDataMapValidator dataMapValidator,
        ModbusPhysicalWriteAuditSink physicalWriteAuditSink,
        ILogger<ModbusTcpService> logger)
    {
        _runtimeService = runtimeService;
        _clientService = clientService;
        _serverService = serverService;
        _optionsMonitor = optionsMonitor;
        _dataMapValidator = dataMapValidator;
        _physicalWriteAuditSink = physicalWriteAuditSink;
        _logger = logger;
        _currentOptions = _optionsMonitor.CurrentValue.Clone();

        ReplaceDataMap(_currentOptions);
        ApplyStatus(_runtimeService.Status);
        ApplySnapshot(_runtimeService.ClientSnapshot);
        ApplySnapshot(_runtimeService.ServerSnapshot);

        _runtimeService.StatusChanged += OnRuntimeStatusChanged;
        _runtimeService.SnapshotChanged += OnRuntimeSnapshotChanged;
    }

    /// <summary>
    /// Текущее состояние высокоуровневого Modbus TCP facade.
    /// </summary>
    public ModbusServiceState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// Уведомляет подписчиков об изменении State Changed в подсистеме Modbus.
    /// </summary>
    public event EventHandler<ModbusServiceState>? StateChanged;

    public ModbusDataSnapshot CurrentSnapshot
    {
        get
        {
            lock (_sync)
            {
                return _currentDataSnapshot;
            }
        }
    }

    public event EventHandler<ModbusDataSnapshot>? SnapshotChanged;

    public ModbusOperationResult ApplyDataMap(IReadOnlyList<ModbusDataPointOptions> dataMap)
    {
        ArgumentNullException.ThrowIfNull(dataMap);

        var nextOptions = _optionsMonitor.CurrentValue.Clone();
        nextOptions.DataMap = dataMap.Select(point => point.Clone()).ToList();
        var validation = ValidateForActiveRoles(nextOptions);
        if (!validation.Succeeded)
        {
            return validation;
        }

        ModbusDataSnapshot snapshot;
        lock (_sync)
        {
            _currentOptions = nextOptions;
            ReplaceDataMap(nextOptions, clearValues: true);
            snapshot = new ModbusDataSnapshot(
                new Dictionary<string, ModbusDataValue>(StringComparer.OrdinalIgnoreCase),
                DateTimeOffset.MinValue,
                _state);
            _currentDataSnapshot = snapshot;
        }

        SnapshotChanged?.Invoke(this, snapshot);
        return ModbusOperationResult.Success();
    }

    /// <summary>
    /// Запускает только клиентскую роль runtime.
    /// </summary>
    public async Task<ModbusOperationResult> StartClientAsync(
        ModbusOptions? options = null,
        CancellationToken ct = default)
    {
        return await StartRoleAsync(ModbusRunMode.Client, options, ct);
    }

    /// <summary>
    /// Запускает только серверную роль runtime.
    /// </summary>
    public async Task<ModbusOperationResult> StartServerAsync(
        ModbusOptions? options = null,
        CancellationToken ct = default)
    {
        return await StartRoleAsync(ModbusRunMode.Server, options, ct);
    }

    /// <summary>
    /// Останавливает активные роли runtime и освобождает сетевые ресурсы.
    /// </summary>
    public async Task<ModbusOperationResult> StopAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);

        try
        {
            await _runtimeService.StopAsync(ct);
            return ModbusOperationResult.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to stop Modbus TCP facade");
            PublishFailure("ModbusStopFailed", "Failed to stop Modbus.", ex.Message);
            return ModbusOperationResult.Failure("ModbusStopFailed", "Failed to stop Modbus.", ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Читает именованную точку из карты данных и преобразует ее к ожидаемому типу.
    /// </summary>
    public Task<ModbusOperationResult<T>> GetAsync<T>(
        string name,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!TryGetPoint(name, out var point, out var failure))
        {
            return Task.FromResult(ModbusOperationResult<T>.Failure(
                failure.ErrorCode ?? "ModbusDataPointMissing",
                failure.ErrorMessage ?? "Modbus data point was not found.",
                failure.ErrorDetails));
        }

        if (!point.IsReadable)
        {
            return Task.FromResult(ModbusOperationResult<T>.Failure(
                "ModbusDataPointNotReadable",
                $"Modbus data point '{point.Name}' is not readable."));
        }

        ModbusDataValue? dataValue;
        lock (_sync)
        {
            _latestValues.TryGetValue(point.Name, out dataValue);
        }

        if (dataValue is null)
        {
            return Task.FromResult(ModbusOperationResult<T>.Failure(
                "ModbusValueUnavailable",
                $"Modbus data point '{point.Name}' has no value yet."));
        }

        return Task.FromResult(TryConvertResult<T>(dataValue.Value, point.Name));
    }

    /// <summary>
    /// Записывает значение в именованную точку карты данных с учетом ее прав доступа и типа.
    /// </summary>
    public Task<ModbusOperationResult> SetAsync<T>(
        string name,
        T value,
        CancellationToken ct = default)
        => SetAsync(name, value, context: null, ct);

    public async Task<ModbusOperationResult> SetAsync<T>(
        string name,
        T value,
        CommandExecutionContext? context,
        CancellationToken ct = default)
    {
        if (!TryGetPoint(name, out var point, out var pointFailure))
        {
            return pointFailure;
        }

        if (!point.IsWritable)
        {
            return ModbusOperationResult.Failure(
                "ModbusDataPointNotWritable",
                $"Modbus data point '{point.Name}' is not writable.");
        }

        object expectedValue = false;
        await _gate.WaitAsync(ct);

        try
        {
            if (!TryBuildWritePayload(point, value, out var coilValue, out var registers, out expectedValue, out var payloadFailure))
            {
                return payloadFailure;
            }

            var role = ResolveWritableRole();
            if (role == ModbusRunMode.None)
            {
                return ModbusOperationResult.Failure(
                    "ModbusNotRunning",
                    "Modbus client or server must be running before writing data.");
            }

            var writeThroughClient = ShouldWriteThroughClient(role);
            var auditRole = writeThroughClient ? ModbusRuntimeRole.Client : ModbusRuntimeRole.Server;
            var endpoint = writeThroughClient ? _currentOptions.Client : _currentOptions.Server;

            if (point.Area == ModbusDataArea.Coil)
            {
                var physicalAddress = endpoint.CoilStartAddress + point.Address;
                var payload = ModbusPhysicalWriteAuditSink.BuildCoilPayload(coilValue);
                if (writeThroughClient)
                {
                    await ExecutePhysicalWriteAsync(
                        context,
                        auditRole,
                        ModbusDataArea.Coil,
                        physicalAddress,
                        quantity: 1,
                        payloadBlob: payload,
                        writeAsync: token => _clientService.WriteCoilAsync(point.Address, coilValue, token),
                        cancellationToken: ct);
                }
                else
                {
                    await ExecutePhysicalWriteAsync(
                        context,
                        auditRole,
                        ModbusDataArea.Coil,
                        physicalAddress,
                        quantity: 1,
                        payloadBlob: payload,
                        writeAsync: token => _serverService.SetCoilAsync(point.Address, coilValue, token),
                        cancellationToken: ct);
                }
            }
            else
            {
                var physicalAddress = endpoint.HoldingRegisterStartAddress + point.Address;
                var payload = ModbusPhysicalWriteAuditSink.BuildRegisterPayload(registers);
                if (registers.Length == 1)
                {
                    if (writeThroughClient)
                    {
                        await ExecutePhysicalWriteAsync(
                            context,
                            auditRole,
                            ModbusDataArea.HoldingRegister,
                            physicalAddress,
                            quantity: 1,
                            payloadBlob: payload,
                            writeAsync: token => _clientService.WriteRegisterAsync(point.Address, registers[0], token),
                            cancellationToken: ct);
                    }
                    else
                    {
                        await ExecutePhysicalWriteAsync(
                            context,
                            auditRole,
                            ModbusDataArea.HoldingRegister,
                            physicalAddress,
                            quantity: 1,
                            payloadBlob: payload,
                            writeAsync: token => _serverService.SetRegisterAsync(point.Address, registers[0], token),
                            cancellationToken: ct);
                    }
                }
                else if (writeThroughClient)
                {
                    await ExecutePhysicalWriteAsync(
                        context,
                        auditRole,
                        ModbusDataArea.HoldingRegister,
                        physicalAddress,
                        quantity: registers.Length,
                        payloadBlob: payload,
                        writeAsync: token => _clientService.WriteRegistersAsync(point.Address, registers, token),
                        cancellationToken: ct);
                }
                else
                {
                    await ExecutePhysicalWriteAsync(
                        context,
                        auditRole,
                        ModbusDataArea.HoldingRegister,
                        physicalAddress,
                        quantity: registers.Length,
                        payloadBlob: payload,
                        writeAsync: token => _serverService.SetRegistersAsync(point.Address, registers, token),
                        cancellationToken: ct);
                }

                lock (_sync)
                {
                    for (var index = 0; index < registers.Length; index++)
                    {
                        _holdingRegisterShadow[point.Address + index] = registers[index];
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to write Modbus data point {DataPoint}", point.Name);
            return ModbusOperationResult.Failure(
                "ModbusWriteFailed",
                $"Failed to write Modbus data point '{point.Name}'.",
                ex.Message);
        }
        finally
        {
            _gate.Release();
        }

        if (point.IsReadable)
        {
            return await WaitForConfirmationAsync(point, expectedValue, ct);
        }

        return ModbusOperationResult.Success();
    }

    /// <summary>
    /// Регистрирует callback для изменений именованной точки Modbus-карты.
    /// </summary>
    public IDisposable Subscribe(string name, Action<ModbusDataValue> onChanged)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(onChanged);

        if (!TryGetPoint(name, out var point, out var failure))
        {
            throw new ArgumentException(failure.ErrorMessage, nameof(name));
        }

        ModbusDataValue? currentValue;
        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(point.Name, out var subscribers))
            {
                subscribers = [];
                _subscriptions[point.Name] = subscribers;
            }

            subscribers.Add(onChanged);
            _latestValues.TryGetValue(point.Name, out currentValue);
        }

        if (currentValue is not null)
        {
            onChanged(currentValue);
        }

        return new Subscription(() => Unsubscribe(point.Name, onChanged));
    }

    /// <summary>
    /// Отписывается от событий, останавливает runtime и освобождает синхронизационные ресурсы.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _runtimeService.StatusChanged -= OnRuntimeStatusChanged;
        _runtimeService.SnapshotChanged -= OnRuntimeSnapshotChanged;
        await StopAsync();
        _gate.Dispose();
    }

    /// <summary>
    /// Запускает одну роль фасада, сохраняя публичный API без команды StartBoth.
    /// </summary>
    private async Task<ModbusOperationResult> StartRoleAsync(
        ModbusRunMode role,
        ModbusOptions? options,
        CancellationToken ct)
    {
        _logger.LogInformation("Modbus facade {Role} start requested", role);
        await _gate.WaitAsync(ct);

        try
        {
            var nextOptions = (options ?? _optionsMonitor.CurrentValue).Clone();
            var validation = _dataMapValidator.Validate(nextOptions, role);
            if (!validation.Succeeded)
            {
                PublishFailure(
                    validation.ErrorCode ?? "ModbusDataMapInvalid",
                    validation.ErrorMessage ?? "Modbus data map is invalid.",
                    validation.ErrorDetails);
                return validation;
            }

            _logger.LogInformation("Modbus facade {Role} validation passed", role);
            _currentOptions = nextOptions;
            ReplaceDataMap(_currentOptions);

            if (role == ModbusRunMode.Client)
            {
                _logger.LogInformation("Modbus facade Client start: stopping server role");
                await _runtimeService.StopServerAsync(ct);
                _logger.LogInformation("Modbus facade Client start: starting client role");
                await _runtimeService.StartClientAsync(_currentOptions, ct);
            }
            else
            {
                _logger.LogInformation("Modbus facade Server start: stopping client role");
                await _runtimeService.StopClientAsync(ct);
                _logger.LogInformation("Modbus facade Server start: starting server role");
                await _runtimeService.StartServerAsync(_currentOptions, ct);
            }

            _logger.LogInformation("Modbus facade {Role} start completed", role);
            return ModbusOperationResult.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to start Modbus TCP facade role {Role}", role);
            PublishFailure("ModbusStartFailed", $"Failed to start Modbus {role}.", ex.Message);
            return ModbusOperationResult.Failure("ModbusStartFailed", $"Failed to start Modbus {role}.", ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnRuntimeStatusChanged(object? sender, ModbusStatus status)
    {
        ApplyStatus(status);
    }

    private void OnRuntimeSnapshotChanged(object? sender, ModbusSnapshot snapshot)
    {
        ApplySnapshot(snapshot);
    }

    /// <summary>
    /// Преобразует статус runtime в состояние фасада, включая поддерживаемое in-process состояние Both.
    /// </summary>
    private void ApplyStatus(ModbusStatus status)
    {
        var clientActive = status.ClientState is not ModbusConnectionState.Stopped;
        var serverActive = status.ServerState is not ModbusConnectionState.Stopped;
        var activeRole = (clientActive, serverActive) switch
        {
            (true, true) => ModbusRunMode.Both,
            (true, false) => ModbusRunMode.Client,
            (false, true) => ModbusRunMode.Server,
            _ => ModbusRunMode.None
        };
        var isWaiting = status.ClientState is ModbusConnectionState.Starting or ModbusConnectionState.Reconnecting
            || status.ServerState is ModbusConnectionState.Starting or ModbusConnectionState.Reconnecting;
        var message = activeRole switch
        {
            ModbusRunMode.Client => status.ClientMessage,
            ModbusRunMode.Server => status.ServerMessage,
            ModbusRunMode.Both => $"{status.ClientMessage} {status.ServerMessage}",
            _ => "Modbus stopped."
        };

        PublishState(new ModbusServiceState(
            activeRole,
            status.ClientState,
            status.ServerState,
            isWaiting,
            message,
            status.LastError,
            status.UpdatedAt));
    }

    /// <summary>
    /// Декодирует снимки в именованные значения и сохраняет видимость снимков сервера в режиме Both.
    /// </summary>
    private void ApplySnapshot(ModbusSnapshot snapshot)
    {
        if (snapshot.Role is not (ModbusRuntimeRole.Client or ModbusRuntimeRole.Server))
        {
            return;
        }

        var activeRole = State.ActiveRole;
        if (activeRole == ModbusRunMode.Client && snapshot.Role != ModbusRuntimeRole.Client)
        {
            return;
        }

        if (activeRole == ModbusRunMode.Server && snapshot.Role != ModbusRuntimeRole.Server)
        {
            return;
        }

        // В режиме Both снимок сервера принимается намеренно: так подтверждение записи
        // видит реальную карту сервера после записи через Modbus TCP клиентом.

        List<ModbusDataPointOptions> points;
        lock (_sync)
        {
            points = _dataMap.Values.Select(point => point.Clone()).ToList();
            for (var index = 0; index < snapshot.HoldingRegisters.Count; index++)
            {
                _holdingRegisterShadow[index] = snapshot.HoldingRegisters[index];
            }
        }

        var cycleValues = new Dictionary<string, ModbusDataValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in points)
        {
            if (!point.IsReadable)
            {
                continue;
            }

            if (TryDecodePoint(point, snapshot, out var dataValue, out var failure))
            {
                cycleValues[dataValue.Name] = dataValue;
                PublishDataValue(dataValue);
            }
            else
            {
                _logger.LogDebug(
                    "Skipping Modbus data point {DataPoint}: {Reason}",
                    point.Name,
                    failure.ErrorMessage);
            }
        }

        PublishDataSnapshot(cycleValues, snapshot.Timestamp);
    }

    /// <summary>
    /// Перестраивает lookup по именам из настроенной карты данных и удаляет устаревшие значения кеша.
    /// </summary>
    private void ReplaceDataMap(ModbusOptions options, bool clearValues = false)
    {
        lock (_sync)
        {
            _dataMap.Clear();
            _holdingRegisterShadow.Clear();
            foreach (var point in options.DataMap)
            {
                _dataMap[point.Name] = point.Clone();
            }

            if (clearValues)
            {
                _latestValues.Clear();
                return;
            }

            var knownNames = new HashSet<string>(_dataMap.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var key in _latestValues.Keys.Where(key => !knownNames.Contains(key)).ToArray())
            {
                _latestValues.Remove(key);
            }
        }
    }

    /// <summary>
    /// Находит настроенную точку данных по имени.
    /// </summary>
    private bool TryGetPoint(
        string name,
        out ModbusDataPointOptions point,
        out ModbusOperationResult failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (_sync)
        {
            if (_dataMap.TryGetValue(name, out var configuredPoint))
            {
                point = configuredPoint.Clone();
                failure = ModbusOperationResult.Success();
                return true;
            }
        }

        point = new ModbusDataPointOptions();
        failure = ModbusOperationResult.Failure(
            "ModbusDataPointMissing",
            $"Modbus data point '{name}' was not found in the configured data map.");
        return false;
    }

    /// <summary>
    /// Декодирует одну настроенную точку данных из сырого снимка Modbus.
    /// </summary>
    private bool TryDecodePoint(
        ModbusDataPointOptions point,
        ModbusSnapshot snapshot,
        out ModbusDataValue dataValue,
        out ModbusOperationResult failure)
    {
        try
        {
            object value;
            if (point.Area == ModbusDataArea.Coil)
            {
                if (point.Address >= snapshot.Coils.Count)
                {
                    dataValue = EmptyValue(point);
                    failure = ModbusOperationResult.Failure(
                        "ModbusSnapshotRangeInvalid",
                        $"Coil data point '{point.Name}' is outside the current snapshot.");
                    return false;
                }

                value = snapshot.Coils[point.Address];
            }
            else
            {
                if (point.Address + point.Length > snapshot.HoldingRegisters.Count)
                {
                    dataValue = EmptyValue(point);
                    failure = ModbusOperationResult.Failure(
                        "ModbusSnapshotRangeInvalid",
                        $"Holding register data point '{point.Name}' is outside the current snapshot.");
                    return false;
                }

                value = point.Type switch
                {
                    ModbusValueType.Bool when point.BitIndex is int bitIndex =>
                        (snapshot.HoldingRegisters[point.Address] & (1 << bitIndex)) != 0,
                    ModbusValueType.UInt16 => snapshot.HoldingRegisters[point.Address],
                    ModbusValueType.Int => ModbusRegistersCodec.DecodeInt(snapshot.HoldingRegisters, point.Address),
                    ModbusValueType.Real => ModbusRegistersCodec.DecodeReal(snapshot.HoldingRegisters, point.Address),
                    ModbusValueType.String => ModbusRegistersCodec.DecodeString(
                        snapshot.HoldingRegisters,
                        point.Address,
                        point.Length),
                    ModbusValueType.Date => ModbusRegistersCodec.DecodeDate(snapshot.HoldingRegisters, point.Address),
                    ModbusValueType.Dword => ModbusRegistersCodec.DecodeDword(snapshot.HoldingRegisters, point.Address),
                    _ => snapshot.HoldingRegisters[point.Address]
                };
            }

            dataValue = new ModbusDataValue(
                point.Name,
                point.Area,
                point.Address,
                point.Length,
                point.Type,
                value,
                snapshot.Timestamp);
            failure = ModbusOperationResult.Success();
            return true;
        }
        catch (Exception ex)
        {
            dataValue = EmptyValue(point);
            failure = ModbusOperationResult.Failure(
                "ModbusValueDecodeFailed",
                $"Failed to decode Modbus data point '{point.Name}'.",
                ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Преобразует типизированное значение в payload Coil или Holding Register для низкоуровневых сервисов.
    /// </summary>
    private bool TryBuildWritePayload<T>(
        ModbusDataPointOptions point,
        T value,
        out bool coilValue,
        out ushort[] registers,
        out object expectedValue,
        out ModbusOperationResult failure)
    {
        coilValue = false;
        registers = [];
        expectedValue = false;

        try
        {
            if (point.Area == ModbusDataArea.Coil)
            {
                coilValue = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                expectedValue = coilValue;
                failure = ModbusOperationResult.Success();
                return true;
            }

            if (point.Type == ModbusValueType.Bool && point.BitIndex is int bitIndex)
            {
                ushort currentWord;
                lock (_sync)
                {
                    if (!_holdingRegisterShadow.TryGetValue(point.Address, out currentWord))
                    {
                        failure = ModbusOperationResult.Failure(
                            "ModbusRegisterShadowUnavailable",
                            $"Holding register {point.Address} has no snapshot for bit write '{point.Name}'.");
                        return false;
                    }
                }

                var bitValue = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                var mask = checked((ushort)(1 << bitIndex));
                var nextWord = bitValue
                    ? (ushort)(currentWord | mask)
                    : (ushort)(currentWord & ~mask);
                registers = [nextWord];
                expectedValue = bitValue;
                failure = ModbusOperationResult.Success();
                return true;
            }

            registers = point.Type switch
            {
                ModbusValueType.UInt16 => [Convert.ToUInt16(value, CultureInfo.InvariantCulture)],
                ModbusValueType.Int => ModbusRegistersCodec.EncodeInt(Convert.ToInt32(value, CultureInfo.InvariantCulture)),
                ModbusValueType.Real => ModbusRegistersCodec.EncodeReal(Convert.ToSingle(value, CultureInfo.InvariantCulture)),
                ModbusValueType.String => ModbusRegistersCodec.EncodeString(
                    Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                    point.Length),
                ModbusValueType.Date => ModbusRegistersCodec.EncodeDate(ToDateTime(value)),
                ModbusValueType.Dword => ModbusRegistersCodec.EncodeDword(Convert.ToUInt32(value, CultureInfo.InvariantCulture)),
                _ => [Convert.ToUInt16(value, CultureInfo.InvariantCulture)]
            };
            expectedValue = DecodeExpectedRegisterValue(point, registers);
            failure = ModbusOperationResult.Success();
            return true;
        }
        catch (Exception ex)
        {
            failure = ModbusOperationResult.Failure(
                "ModbusValueEncodeFailed",
                $"Failed to encode value for Modbus data point '{point.Name}'.",
                ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Ждет, пока readable-точка данных опубликует только что записанное значение.
    /// </summary>
    private async Task<ModbusOperationResult> WaitForConfirmationAsync(
        ModbusDataPointOptions point,
        object expectedValue,
        CancellationToken ct)
    {
        var timeoutMs = Math.Max(0, _currentOptions.WriteConfirmationTimeoutMs);
        if (timeoutMs == 0)
        {
            return ModbusOperationResult.Success();
        }

        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTimeOffset.UtcNow <= deadline)
        {
            ct.ThrowIfCancellationRequested();

            ModbusDataValue? currentValue;
            lock (_sync)
            {
                _latestValues.TryGetValue(point.Name, out currentValue);
            }

            if (currentValue is not null && ValuesEqual(currentValue.Value, expectedValue))
            {
                return ModbusOperationResult.Success();
            }

            await Task.Delay(50, ct);
        }

        return ModbusOperationResult.Failure(
            "ModbusWriteConfirmationTimeout",
            $"Write to Modbus data point '{point.Name}' was not confirmed by the next snapshot.");
    }

    private async Task ExecutePhysicalWriteAsync(
        CommandExecutionContext? context,
        ModbusRuntimeRole role,
        ModbusDataArea area,
        int address,
        int quantity,
        IReadOnlyList<byte> payloadBlob,
        Func<CancellationToken, Task> writeAsync,
        CancellationToken cancellationToken)
    {
        var attemptedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            await writeAsync(cancellationToken).ConfigureAwait(false);
            await _physicalWriteAuditSink.RecordAsync(
                context,
                role,
                area,
                address,
                quantity,
                payloadBlob,
                attemptedAtUtc,
                DateTimeOffset.UtcNow,
                succeeded: true,
                errorCode: null,
                errorMessage: null,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await _physicalWriteAuditSink.RecordAsync(
                context,
                role,
                area,
                address,
                quantity,
                payloadBlob,
                attemptedAtUtc,
                DateTimeOffset.UtcNow,
                succeeded: false,
                "ModbusWriteCanceled",
                "Modbus write was canceled.",
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _physicalWriteAuditSink.RecordAsync(
                context,
                role,
                area,
                address,
                quantity,
                payloadBlob,
                attemptedAtUtc,
                DateTimeOffset.UtcNow,
                succeeded: false,
                "ModbusWriteFailed",
                ex.Message,
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Определяет, какой низкоуровневый сервис должен принять запись при текущих запущенных ролях.
    /// </summary>
    private ModbusRunMode ResolveWritableRole()
    {
        var state = State;
        var clientRunning = state.ClientState == ModbusConnectionState.Running;
        var serverRunning = state.ServerState == ModbusConnectionState.Running;

        return (clientRunning, serverRunning) switch
        {
            (true, true) => ModbusRunMode.Both,
            (true, false) => ModbusRunMode.Client,
            (false, true) => ModbusRunMode.Server,
            _ => ModbusRunMode.None
        };
    }

    /// <summary>
    /// Использует клиентский путь записи, когда клиент запущен, включая локальный режим Both.
    /// </summary>
    private static bool ShouldWriteThroughClient(ModbusRunMode role)
        => role is ModbusRunMode.Client or ModbusRunMode.Both;

    /// <summary>
    /// Сохраняет декодированное значение и уведомляет подписчиков только при изменении.
    /// </summary>
    private void PublishDataValue(ModbusDataValue dataValue)
    {
        List<Action<ModbusDataValue>> subscribers = [];

        lock (_sync)
        {
            if (_latestValues.TryGetValue(dataValue.Name, out var previous)
                && ValuesEqual(previous.Value, dataValue.Value))
            {
                return;
            }

            _latestValues[dataValue.Name] = dataValue;

            if (_subscriptions.TryGetValue(dataValue.Name, out var pointSubscribers))
            {
                subscribers = pointSubscribers.ToList();
            }
        }

        foreach (var subscriber in subscribers)
        {
            try
            {
                subscriber(dataValue);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Modbus subscription handler failed for {DataPoint}", dataValue.Name);
            }
        }
    }

    private void PublishFailure(string code, string message, string? details)
    {
        var current = State;
        PublishState(current with
        {
            LastError = string.IsNullOrWhiteSpace(details) ? message : $"{message} {details}",
            Message = message,
            UpdatedAt = DateTimeOffset.Now
        });
    }

    private void PublishState(ModbusServiceState state)
    {
        lock (_sync)
        {
            _state = state;
            _currentDataSnapshot = _currentDataSnapshot with { State = state };
        }

        StateChanged?.Invoke(this, state);
    }

    private void PublishDataSnapshot(
        IReadOnlyDictionary<string, ModbusDataValue> values,
        DateTimeOffset timestamp)
    {
        ModbusDataSnapshot snapshot;
        lock (_sync)
        {
            snapshot = new ModbusDataSnapshot(
                new Dictionary<string, ModbusDataValue>(values, StringComparer.OrdinalIgnoreCase),
                timestamp,
                _state);
            _currentDataSnapshot = snapshot;
        }

        try
        {
            SnapshotChanged?.Invoke(this, snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Modbus data snapshot handler failed");
        }
    }

    private ModbusOperationResult ValidateForActiveRoles(ModbusOptions options)
    {
        var activeMode = State.ActiveRole;
        if (activeMode is ModbusRunMode.Client or ModbusRunMode.Server)
        {
            return _dataMapValidator.Validate(options, activeMode);
        }

        if (activeMode == ModbusRunMode.Both)
        {
            var client = _dataMapValidator.Validate(options, ModbusRunMode.Client);
            return client.Succeeded
                ? _dataMapValidator.Validate(options, ModbusRunMode.Server)
                : client;
        }

        return _dataMapValidator.Validate(options, ModbusRunMode.None);
    }

    private void Unsubscribe(string name, Action<ModbusDataValue> onChanged)
    {
        lock (_sync)
        {
            if (_subscriptions.TryGetValue(name, out var subscribers))
            {
                subscribers.Remove(onChanged);
                if (subscribers.Count == 0)
                {
                    _subscriptions.Remove(name);
                }
            }
        }
    }

    private static ModbusDataValue EmptyValue(ModbusDataPointOptions point)
        => new(
            point.Name,
            point.Area,
            point.Address,
            point.Length,
            point.Type,
            null,
            DateTimeOffset.Now);

    private static object DecodeExpectedRegisterValue(ModbusDataPointOptions point, IReadOnlyList<ushort> registers)
        => point.Type switch
        {
            ModbusValueType.UInt16 => registers[0],
            ModbusValueType.Int => ModbusRegistersCodec.DecodeInt(registers, 0),
            ModbusValueType.Real => ModbusRegistersCodec.DecodeReal(registers, 0),
            ModbusValueType.String => ModbusRegistersCodec.DecodeString(registers, 0, point.Length),
            ModbusValueType.Date => ModbusRegistersCodec.DecodeDate(registers, 0),
            ModbusValueType.Dword => ModbusRegistersCodec.DecodeDword(registers, 0),
            _ => registers[0]
        };

    private static ModbusOperationResult<T> TryConvertResult<T>(object? value, string name)
    {
        try
        {
            if (value is T typed)
            {
                return ModbusOperationResult<T>.Success(typed);
            }

            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            object? converted = targetType == typeof(string)
                ? Convert.ToString(value, CultureInfo.InvariantCulture)
                : targetType == typeof(DateTime)
                    ? ToDateTime(value)
                    : Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);

            return ModbusOperationResult<T>.Success((T)converted!);
        }
        catch (Exception ex)
        {
            return ModbusOperationResult<T>.Failure(
                "ModbusValueConversionFailed",
                $"Failed to convert Modbus data point '{name}' to {typeof(T).Name}.",
                ex.Message);
        }
    }

    private static DateTime ToDateTime<T>(T value)
    {
        if (value is DateTime dateTime)
        {
            return dateTime;
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return DateTime.Parse(text ?? string.Empty, CultureInfo.InvariantCulture);
    }

    private static bool ValuesEqual(object? left, object? right)
    {
        if (left is float leftFloat && right is float rightFloat)
        {
            return Math.Abs(leftFloat - rightFloat) < 0.0001f;
        }

        if (left is DateTime leftDate && right is DateTime rightDate)
        {
            return leftDate.ToUniversalTime() == rightDate.ToUniversalTime();
        }

        return Equals(left, right);
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        /// <summary>
        /// Освобождает ресурс и предотвращает повторное выполнение связанного действия.
        /// </summary>
        public void Dispose()
        {
            Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }
}
