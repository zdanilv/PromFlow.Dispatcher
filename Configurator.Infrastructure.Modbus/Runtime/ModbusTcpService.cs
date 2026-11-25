using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Infrastructure.Modbus.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Diagnostics;

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
    private readonly ILogger<ModbusTcpService> _logger;
    private readonly ModbusPriorityGate _gate = new();
    private readonly SemaphoreSlim _dataMapChangeGate = new(1, 1);
    private readonly object _sync = new();
    private readonly Dictionary<string, ModbusDataPointOptions> _dataMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UncertainCommand> _uncertainCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModbusDataValue> _latestValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Action<ModbusDataValue>>> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, ushort> _holdingRegisterShadow = [];
    private int _disposed;
    private ModbusBitAddressOptions? _activeAcknowledgement;
    private ModbusOptions _currentOptions;
    private ModbusServiceState _state = ModbusServiceState.Stopped;
    private ModbusDataSnapshot _currentDataSnapshot = ModbusDataSnapshot.Empty;
    private long _dataGeneration;
    private long _snapshotReadNotBefore;
    private readonly Dictionary<ModbusRuntimeRole, long> _lastReadSequences = [];
    private ModbusRuntimeRole _shadowRole = ModbusRuntimeRole.Runtime;
    private Guid _shadowSessionId;
    private bool _shadowIsGood;
    private TaskCompletionSource _observationChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<ModbusRuntimeRole, ReadObservation> _readObservations = [];
    private readonly Dictionary<ModbusRuntimeRole, Guid> _sourceSessionIds = [];
    private readonly Dictionary<ModbusRuntimeRole, long> _sourceGenerations = [];
    private readonly Dictionary<ModbusRuntimeRole, ModbusSnapshot> _acceptedSnapshots = [];

    private sealed record ReadObservation(ModbusSnapshot Source,
        IReadOnlyDictionary<string, ModbusDataValue> Values, long Generation);

    private sealed record WriteConfirmationContext(ModbusDataPointOptions Point, ModbusRuntimeRole Role,
        ModbusOptions RuntimeOptions, ModbusEndpointOptions Endpoint, long Generation,
        long SourceGeneration, long PreviousReadSequence, Guid PreviousSourceSessionId, int TimeoutMs);

    /// <summary>
    /// Создает фасад и подписывает его на связанный Modbus runtime.
    /// </summary>
    public ModbusTcpService(
        IModbusRuntimeService runtimeService,
        IModbusClientService clientService,
        IModbusServerService serverService,
        IOptionsMonitor<ModbusOptions> optionsMonitor,
        IModbusDataMapValidator dataMapValidator,
        ILogger<ModbusTcpService> logger)
    {
        _runtimeService = runtimeService;
        _clientService = clientService;
        _serverService = serverService;
        _optionsMonitor = optionsMonitor;
        _dataMapValidator = dataMapValidator;
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
            var acknowledgement = ValidateAgainstActiveAcknowledgement(nextOptions);
            if (!acknowledgement.Succeeded)
                return acknowledgement;

            _currentOptions = nextOptions;
            ReplaceDataMap(nextOptions);
            snapshot = new ModbusDataSnapshot(
                new Dictionary<string, ModbusDataValue>(StringComparer.OrdinalIgnoreCase),
                DateTimeOffset.MinValue,
                _state) { PublicationSequence = ModbusObservationSequence.Next() };
            _currentDataSnapshot = snapshot;
        }

        SnapshotChanged?.Invoke(this, snapshot);
        return ModbusOperationResult.Success();
    }

    public async Task<T> RunDataMapChangeAsync<T>(Func<Task<T>> change, CancellationToken cancellationToken = default)
    {
        await _dataMapChangeGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return await change();
        }
        finally
        {
            _dataMapChangeGate.Release();
        }
    }

    internal async Task<ModbusOperationResult> ActivateAcknowledgementAsync(
        ModbusBitAddressOptions address,
        Func<Task<ModbusOperationResult>> activate,
        CancellationToken cancellationToken)
    {
        try
        {
            return await RunDataMapChangeAsync(
                () => ActivateAcknowledgementCoreAsync(address, activate, cancellationToken), cancellationToken);
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0)
        {
            return ModbusOperationResult.Failure("ModbusNotRunning", "Modbus is shutting down.");
        }
    }

    private async Task<ModbusOperationResult> ActivateAcknowledgementCoreAsync(
        ModbusBitAddressOptions address,
        Func<Task<ModbusOperationResult>> activate,
        CancellationToken cancellationToken)
    {
        Task<ModbusOperationResult> acknowledgement;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var validation = ModbusAlarmMapValidator.ValidateAcknowledgementAddress(address, _optionsMonitor.CurrentValue.DataMap);
            if (!validation.Succeeded)
                return validation;

            lock (_sync)
            {
                validation = ModbusAlarmMapValidator.ValidateAcknowledgementAddress(address, _dataMap.Values);
                if (!validation.Succeeded)
                    return validation;

                // A map replacement must leave this address available until the falling edge finishes.
                _activeAcknowledgement = address;
            }
            acknowledgement = activate();
        }
        finally
        {
            _gate.Release();
        }

        return await acknowledgement.ConfigureAwait(false);
    }

    internal void ReleaseAcknowledgementAddress(ModbusBitAddressOptions address)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_activeAcknowledgement, address))
                _activeAcknowledgement = null;
        }
    }

    // Called with _sync held so a concurrent activation cannot reserve the new mapping.
    private ModbusOperationResult ValidateAgainstActiveAcknowledgement(ModbusOptions options) =>
        _activeAcknowledgement is { } address
            ? ModbusAlarmMapValidator.ValidateAcknowledgementAddress(address, options.DataMap)
            : ModbusOperationResult.Success();

    public Task<ModbusOperationResult> RemovePointWhileStoppedAsync(
        string name,
        Func<CancellationToken, Task<bool>> persist,
        CancellationToken cancellationToken = default) =>
        RunDataMapChangeAsync(() => RemovePointCoreAsync(name, persist, cancellationToken), cancellationToken);

    private async Task<ModbusOperationResult> RemovePointCoreAsync(
        string name, Func<CancellationToken, Task<bool>> persist, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var removed = false;
            var stopped = await _runtimeService.TryRunWhileStoppedAsync(async ct =>
            {
                if (!await persist(ct))
                    return;

                RemovePoint(name);
                removed = true;
            }, cancellationToken);

            if (!stopped)
                return ModbusOperationResult.Failure("ModbusMustBeStopped",
                    "Остановите клиент и сервер Modbus TCP, затем повторите удаление.");

            return removed ? ModbusOperationResult.Success()
                : ModbusOperationResult.Failure("ModbusPointChanged",
                    "Точка Modbus изменилась, была удалена или снова используется в RouteMap. Проверьте обновлённый список.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private void RemovePoint(string name)
    {
        ModbusDataSnapshot snapshot;
        lock (_sync)
        {
            var options = _currentOptions.Clone();
            options.DataMap.RemoveAll(point => string.Equals(point.Name, name, StringComparison.OrdinalIgnoreCase));
            _currentOptions = options;
            _dataMap.Remove(name);
            _latestValues.Remove(name);
            var values = new Dictionary<string, ModbusDataValue>(_currentDataSnapshot.Values, StringComparer.OrdinalIgnoreCase);
            values.Remove(name);
            snapshot = _currentDataSnapshot with { Values = values, PublicationSequence = ModbusObservationSequence.Next() };
            _currentDataSnapshot = snapshot;
        }

        NotifyDataSnapshot(snapshot);
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
        CancellationToken ct = default) => SetAsync(name, value, null, false, false, null, null, ct);

    internal const string PulseMappingRejected = "ModbusPulseMappingRejected";

    internal readonly record struct PulseTarget(ModbusRunMode Role, ModbusOptions RuntimeOptions,
        ModbusEndpointOptions Endpoint, long Generation, long SourceGeneration, Guid Session);

    internal readonly record struct ResetFeedback(ModbusDataPointOptions Point, int StaleAfterMs);

    private sealed record UncertainCommand(ModbusDataPointOptions Command, ResetFeedback Feedback,
        PulseTarget OriginalTarget, long AfterSequence);

    internal PulseTarget CapturePulseTarget()
    {
        lock (_sync)
        {
            var role = WriteRole(ResolveWritableRole());
            var source = RuntimeRole(role);
            var options = _runtimeService.CurrentOptions;
            return new(role, options, GetEndpoint(options, source).Clone(), _dataGeneration,
                _sourceGenerations.GetValueOrDefault(source), _sourceSessionIds.GetValueOrDefault(source));
        }
    }

    internal Task<ModbusOperationResult> SetPriorityAsync(ModbusDataPointOptions point, object? value, CancellationToken ct) =>
        SetAsync(point.Name, value, (point, CapturePulseTarget()), true, false, null, null, ct);

    internal Task<ModbusOperationResult> SetPulseEdgeAsync(
        ModbusDataPointOptions point, PulseTarget target, bool value, bool priority,
        ResetFeedback? feedback, Action<ModbusWriteReceipt>? submitted, CancellationToken ct) =>
        SetAsync(point.Name, value, (point, target), priority, true, feedback, submitted, ct);

    private async Task<ModbusOperationResult> SetAsync<T>(
        string name, T value, (ModbusDataPointOptions Point, PulseTarget Target)? expected,
        bool priority, bool pulse, ResetFeedback? feedback, Action<ModbusWriteReceipt>? submitted, CancellationToken ct)
    {
        var target = expected?.Target ?? CapturePulseTarget();
        var source = RuntimeRole(target.Role);
        var queuedAt = Stopwatch.GetTimestamp();
        WriteConfirmationContext? confirmation = null;
        object expectedValue = false;
        Task<ModbusWriteReceipt>? pendingWrite = null;
        ModbusDataPointOptions point = null!;

        try
        {
            await _gate.WaitAsync(ct, priority).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();
                if (!TryGetPoint(name, out point, out var failure))
                    return expected is null ? failure : RejectPulseMapping(failure.ErrorMessage);
                if (!point.IsWritable)
                    return ModbusOperationResult.Failure("ModbusDataPointNotWritable", $"Modbus data point '{name}' is not writable.");
                if (target.Role == ModbusRunMode.None)
                    return ModbusOperationResult.Failure("ModbusNotRunning", "Modbus client or server must be running before writing data.");

                var writer = ShouldWriteThroughClient(target.Role)
                    ? _clientService as IModbusObservedWriter
                    : _serverService as IModbusObservedWriter;
                if (writer is not null)
                {
                    // Enqueue while admitting the write, but await transport and readback after
                    // releasing the facade. An urgent write can overtake a queued normal write.
                    ModbusWriteAddress? response = feedback is { } reset && value is true
                        ? new(reset.Point.Area, reset.Point.Address, reset.Point.BitIndex)
                        : null;
                    pendingWrite = writer.WriteObservedAsync(new(point.Area, point.Address, point.BitIndex), response,
                        priority, pulse, ValidateBeforeRead, Prepare, Commit, ct);
                }
                else
                {
                    var payload = Prepare(new(source, target.Session, 0), alreadyHigh: false);
                    if (ShouldWriteThroughClient(target.Role))
                    {
                        if (point.Area == ModbusDataArea.Coil)
                            await _clientService.WriteCoilAsync(point.Address, payload.Coil, ct).ConfigureAwait(false);
                        else if (payload.Registers.Length == 1)
                            await _clientService.WriteRegisterAsync(point.Address, payload.Registers[0], ct).ConfigureAwait(false);
                        else
                            await _clientService.WriteRegistersAsync(point.Address, payload.Registers, ct).ConfigureAwait(false);
                    }
                    else if (point.Area == ModbusDataArea.Coil)
                        await _serverService.SetCoilAsync(point.Address, payload.Coil, ct).ConfigureAwait(false);
                    else if (payload.Registers.Length == 1)
                        await _serverService.SetRegisterAsync(point.Address, payload.Registers[0], ct).ConfigureAwait(false);
                    else
                        await _serverService.SetRegistersAsync(point.Address, payload.Registers, ct).ConfigureAwait(false);

                    Commit(payload);
                }
            }
            finally
            {
                _gate.Release();
            }

            ModbusWriteReceipt? receipt = pendingWrite is null ? null : await pendingWrite.ConfigureAwait(false);
            if (point.IsReadable && !(point.WriteMode == ModbusWriteMode.Pulse && expectedValue is true))
                return await WaitForConfirmationAsync(confirmation!, receipt, expectedValue, ct).ConfigureAwait(false);
            return ModbusOperationResult.Success();
        }
        catch (ModbusWriteNotSubmittedException ex)
        {
            return expected is null && ex.ErrorCode is { } code
                ? ModbusOperationResult.Failure(code, ex.Message)
                : RejectPulseMapping(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to write Modbus data point {DataPoint}", name);
            return ModbusOperationResult.Failure("ModbusWriteFailed", $"Failed to write Modbus data point '{name}'.", ex.Message);
        }

        void ValidateBeforeRead(Guid session)
        {
            if (priority)
                _logger.LogDebug("Modbus command {SignalId} waited {QueueMs:F1} ms for transport", name,
                    Stopwatch.GetElapsedTime(queuedAt).TotalMilliseconds);
            lock (_sync)
            {
                ValidateActiveTarget(session);
                if (point.Type == ModbusValueType.Bool && point.BitIndex.HasValue
                    && !_holdingRegisterShadow.ContainsKey(point.Address))
                    throw new ModbusWriteNotSubmittedException(
                        $"Holding register {point.Address} has no snapshot for bit write '{point.Name}'.",
                        "ModbusRegisterShadowUnavailable");
            }
        }

        void ValidateActiveTarget(Guid session)
        {
            if (!_dataMap.TryGetValue(name, out var active) || !point.HasSameMapping(active)
                || (expected is { } captured && !point.HasSameMapping(captured.Point)))
                throw new ModbusWriteNotSubmittedException($"Active mapping for Modbus signal '{name}' differs from Modbus.DataMap.");
            if (!IsTargetCurrent(target) || (target.Session != Guid.Empty && target.Session != session))
                throw new ModbusWriteNotSubmittedException($"The Modbus role, endpoint or session changed before writing '{name}'.");
        }

        ModbusWritePayload Prepare(ModbusWriteReceipt receipt, bool alreadyHigh)
        {
            ct.ThrowIfCancellationRequested();
            lock (_sync)
            {
                ValidateActiveTarget(receipt.SourceSessionId);

                if (pulse)
                {
                    var validation = ValidatePulseMapping(point, expected!.Value.Point, target, target.Role);
                    if (!validation.Succeeded)
                        throw new ModbusWriteNotSubmittedException(validation.ErrorMessage!);
                }

                if (value is true && _uncertainCommands.TryGetValue(name, out var uncertain))
                {
                    var staleAfter = uncertain.Feedback.StaleAfterMs;
                    if (target.Role != uncertain.OriginalTarget.Role
                        || !SameEndpoint(target.Endpoint, uncertain.OriginalTarget.Endpoint)
                        || ReadFreshBool(target, uncertain.Command, staleAfter) is not { Value: false } oldCommand
                        || ReadFreshBool(target, uncertain.Feedback.Point, staleAfter) is not { Value: false } oldResponse
                        || oldCommand.Sequence <= uncertain.AfterSequence || oldResponse.Sequence <= uncertain.AfterSequence)
                        throw new ModbusWriteNotSubmittedException("Состояние предыдущей команды неизвестно. Нужны свежие нули команды и ответа ПЛК.");
                    _uncertainCommands.Remove(name);
                }

                if (feedback is { } reset && value is true)
                {
                    var feedbackProblem = ModbusResetFeedbackValidation.GetError(point.Name, reset.Point.Name, _currentOptions);
                    if (feedbackProblem is not null)
                        throw new ModbusWriteNotSubmittedException(feedbackProblem);
                    if (!_dataMap.TryGetValue(reset.Point.Name, out var activeFeedback) || !reset.Point.HasSameMapping(activeFeedback))
                        throw new ModbusWriteNotSubmittedException("Привязка ответа ПЛК изменилась перед отправкой команды.");

                    if (receipt.SourceSessionId == Guid.Empty || receipt.Sequence <= 0 || alreadyHigh ||
                        ReadFreshBool(target, reset.Point, reset.StaleAfterMs) is not { Value: false } ||
                        ReadFreshBool(target, point, reset.StaleAfterMs) is not { Value: false })
                        throw new ModbusWriteNotSubmittedException("Команда и ответ ПЛК должны иметь свежее достоверное значение 0 перед отправкой.");
                }

                if (!TryBuildWritePayload(point, value, out var coil, out var registers, out expectedValue, out var failure))
                    throw new ModbusWriteNotSubmittedException(failure.ErrorMessage!, failure.ErrorCode);

                _readObservations.TryGetValue(source, out var previousRead);
                var timeout = Math.Max(0, _currentOptions.WriteConfirmationTimeoutMs);
                if (feedback is not null && value is false && timeout == 0)
                    timeout = 2000;
                confirmation = new(point, source, target.RuntimeOptions, target.Endpoint, target.Generation,
                    target.SourceGeneration, previousRead?.Source.GetReadState(point.Area).SuccessfulReadSequence ?? 0,
                    previousRead?.Source.SourceSessionId ?? Guid.Empty, timeout);
                submitted?.Invoke(receipt);
                return new(coil, registers, point.BitIndex, expectedValue is true);
            }
        }

        void Commit(ModbusWritePayload payload)
        {
            if (priority)
                _logger.LogDebug("Modbus command {SignalId} wrote value {Value} after {ElapsedMs:F1} ms",
                    name, value, Stopwatch.GetElapsedTime(queuedAt).TotalMilliseconds);
            lock (_sync)
            {
                for (var index = 0; _shadowIsGood && IsTargetCurrent(target)
                    && _shadowRole == source && index < payload.Registers.Length; index++)
                    _holdingRegisterShadow[point.Address + index] = payload.Registers[index];
            }
        }
    }

    internal void MarkCommandUncertain(ModbusDataPointOptions command, ResetFeedback feedback, PulseTarget originalTarget)
    {
        lock (_sync)
            _uncertainCommands[command.Name] = new(command.Clone(), feedback, originalTarget, ModbusObservationSequence.Next());
    }

    private bool IsTargetCurrent(PulseTarget target)
    {
        var source = RuntimeRole(target.Role);
        var options = _runtimeService.CurrentOptions;
        return target.Role != ModbusRunMode.None && target.Role == WriteRole(ResolveWritableRole())
            && ReferenceEquals(options, target.RuntimeOptions) && SameEndpoint(GetEndpoint(options, source), target.Endpoint)
            && target.Generation == _dataGeneration
            && target.SourceGeneration == _sourceGenerations.GetValueOrDefault(source)
            && (target.Session == Guid.Empty || target.Session == _sourceSessionIds.GetValueOrDefault(source));
    }

    private static ModbusEndpointOptions GetEndpoint(ModbusOptions options, ModbusRuntimeRole role) =>
        role == ModbusRuntimeRole.Client ? options.Client : options.Server;

    private (bool Value, long Sequence)? ReadFreshBool(PulseTarget target, ModbusDataPointOptions point, int staleAfterMs)
    {
        var source = RuntimeRole(target.Role);
        if (!_readObservations.TryGetValue(source, out var read) || read.Generation != target.Generation
            || read.Source.SourceSessionId != target.Session)
            return null;

        var area = read.Source.GetReadState(point.Area);
        var age = DateTimeOffset.UtcNow - area.LastSuccessfulReadAt;
        if (!area.IsGood || area.SuccessfulReadSequence <= 0 || age < TimeSpan.Zero
            || age.TotalMilliseconds > staleAfterMs
            || !TryDecodePoint(point, read.Source, out var data, out _) || data.Value is not bool value)
            return null;

        return (value, area.SuccessfulReadSequence);
    }

    internal async Task WaitForResetFeedbackAsync(PulseTarget target, ResetFeedback feedback, bool expected,
        long afterSequence, int timeoutMs, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeoutMs);
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                Task updated;
                lock (_sync)
                {
                    if (!IsTargetCurrent(target) || !_dataMap.TryGetValue(feedback.Point.Name, out var active)
                        || !active.HasSameMapping(feedback.Point))
                        throw new InvalidOperationException("Карта или сессия Modbus изменилась при ожидании ответа ПЛК.");
                    if (ReadFreshBool(target, feedback.Point, feedback.StaleAfterMs) is { } read
                        && read.Sequence > afterSequence && read.Value == expected)
                    {
                        _logger.LogDebug("PLC response {SignalId} became {Value}", feedback.Point.Name, expected);
                        return;
                    }

                    updated = _observationChanged.Task;
                }

                await updated.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(expected ? "ПЛК не подтвердил приём команды." : "ПЛК не снял ответ после сброса команды.");
        }
    }

    private void NotifyObservationChanged()
    {
        _observationChanged.TrySetResult();
        _observationChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private ModbusOperationResult ValidatePulseMapping(
        ModbusDataPointOptions active, ModbusDataPointOptions expected, PulseTarget target, ModbusRunMode role)
    {
        if (active.Area != expected.Area || active.Address != expected.Address || active.BitIndex != expected.BitIndex
            || active.Length != expected.Length || active.Type != expected.Type || active.Access != expected.Access
            || active.WriteMode != expected.WriteMode || active.PulseDurationMs != expected.PulseDurationMs)
        {
            return RejectPulseMapping($"Active mapping for Modbus signal '{expected.Name}' differs from Modbus.DataMap.");
        }

        var runtimeOptions = _runtimeService.CurrentOptions;
        if (role == ModbusRunMode.None || role != target.Role || !ReferenceEquals(runtimeOptions, target.RuntimeOptions))
            return RejectPulseMapping($"The active Modbus endpoint for pulse '{expected.Name}' is unavailable or changed.");

        var options = new ModbusOptions
        {
            Client = runtimeOptions.Client,
            Server = runtimeOptions.Server,
            DataMap = _currentOptions.DataMap,
        };
        var validation = _dataMapValidator.Validate(options, role);
        return validation.Succeeded ? validation : RejectPulseMapping(validation.ErrorMessage);
    }

    private static ModbusOperationResult RejectPulseMapping(string? message) => ModbusOperationResult.Failure(
        PulseMappingRejected, message ?? "Invalid Modbus pulse mapping.");

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
        await _dataMapChangeGate.WaitAsync();
        try
        {
        }
        finally
        {
            // Queued callers must acquire the gate to observe _disposed.
            _dataMapChangeGate.Release();
        }
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
            ModbusDataSnapshot resetSnapshot;
            lock (_sync)
            {
                var acknowledgement = ValidateAgainstActiveAcknowledgement(nextOptions);
                if (!acknowledgement.Succeeded)
                    return acknowledgement;

                _currentOptions = nextOptions;
                ReplaceDataMap(_currentOptions);
                resetSnapshot = ModbusDataSnapshot.Empty with { State = _state, PublicationSequence = ModbusObservationSequence.Next() };
                _currentDataSnapshot = resetSnapshot;
            }
            NotifyDataSnapshot(resetSnapshot);

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
        if (activeRole == ModbusRunMode.None)
            return;
        if (activeRole == ModbusRunMode.Client && snapshot.Role != ModbusRuntimeRole.Client)
        {
            return;
        }

        if (activeRole == ModbusRunMode.Server && snapshot.Role != ModbusRuntimeRole.Server)
        {
            return;
        }

        // Both retains server snapshot visibility; read validity stays with its source role.

        List<ModbusDataPointOptions> points;
        long generation;
        var coilsState = snapshot.GetReadState(ModbusDataArea.Coil);
        var registersState = snapshot.GetReadState(ModbusDataArea.HoldingRegister);
        lock (_sync)
        {
            if (_state.ActiveRole == ModbusRunMode.None
                || (_state.ActiveRole == ModbusRunMode.Client && snapshot.Role != ModbusRuntimeRole.Client)
                || (_state.ActiveRole == ModbusRunMode.Server && snapshot.Role != ModbusRuntimeRole.Server))
                return;
            if (snapshot.ReadSequence > 0)
            {
                if (snapshot.ReadSequence <= _snapshotReadNotBefore
                    || (_lastReadSequences.TryGetValue(snapshot.Role, out var lastSequence) && snapshot.ReadSequence <= lastSequence))
                    return;
                _lastReadSequences[snapshot.Role] = snapshot.ReadSequence;
            }

            if (_sourceSessionIds.TryGetValue(snapshot.Role, out var previousSession) && previousSession != snapshot.SourceSessionId)
                _sourceGenerations[snapshot.Role] = _sourceGenerations.GetValueOrDefault(snapshot.Role) + 1;
            _sourceSessionIds[snapshot.Role] = snapshot.SourceSessionId;
            _acceptedSnapshots[snapshot.Role] = snapshot;

            generation = _dataGeneration;
            points = _dataMap.Values.Select(point => point.Clone()).ToList();
            // Both retains server visibility, but server data must never seed client RMW.
            if (snapshot.Role == RuntimeRole(WriteRole(ResolveWritableRole())))
            {
                _holdingRegisterShadow.Clear();
                _shadowRole = snapshot.Role;
                _shadowSessionId = snapshot.SourceSessionId;
                _shadowIsGood = registersState.IsGood && (registersState.SuccessfulReadSequence == 0
                    || registersState.SuccessfulReadSequence > _snapshotReadNotBefore);
                if (_shadowIsGood)
                {
                    for (var index = 0; index < snapshot.HoldingRegisters.Count; index++)
                        _holdingRegisterShadow[index] = snapshot.HoldingRegisters[index];
                }
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
                PublishDataValue(dataValue, generation, snapshot);
            }
            else
            {
                _logger.LogDebug(
                    "Skipping Modbus data point {DataPoint}: {Reason}",
                    point.Name,
                    failure.ErrorMessage);
            }
        }

        lock (_sync)
        {
            if (generation != _dataGeneration || !IsAcceptedSnapshot(snapshot))
                return;
            foreach (var point in points.Where(point => point.IsReadable && !cycleValues.ContainsKey(point.Name)))
                _latestValues.Remove(point.Name);
            if (!_readObservations.TryGetValue(snapshot.Role, out var previous)
                || snapshot.ReadSequence == 0 || snapshot.ReadSequence >= previous.Source.ReadSequence)
                _readObservations[snapshot.Role] = new(snapshot, cycleValues, generation);
            NotifyObservationChanged();
        }
        PublishDataSnapshot(cycleValues, snapshot, generation);
    }

    /// <summary>
    /// Перестраивает lookup по именам из настроенной карты данных и удаляет устаревшие значения кеша.
    /// </summary>
    private void ReplaceDataMap(ModbusOptions options)
    {
        lock (_sync)
        {
            _dataMap.Clear();
            _dataGeneration++;
            NotifyObservationChanged();
            _snapshotReadNotBefore = ModbusObservationSequence.Next();
            _holdingRegisterShadow.Clear();
            _shadowIsGood = false;
            _latestValues.Clear();
            _readObservations.Clear();
            _sourceSessionIds.Clear();
            foreach (var point in options.DataMap)
            {
                _dataMap[point.Name] = point.Clone();
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
            var readState = snapshot.GetReadState(point.Area);
            if (!readState.IsGood || (readState.SuccessfulReadSequence > 0 && readState.SuccessfulReadSequence <= _snapshotReadNotBefore))
            {
                dataValue = EmptyValue(point);
                failure = ModbusOperationResult.Failure("ModbusAreaReadUnavailable",
                    $"The latest read of {point.Area} for '{point.Name}' is not trustworthy.");
                return false;
            }
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
                    ModbusValueType.UInt16 or ModbusValueType.Word => snapshot.HoldingRegisters[point.Address],
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
                readState.LastSuccessfulReadAt);
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
                ModbusValueType.UInt16 or ModbusValueType.Word => [Convert.ToUInt16(value, CultureInfo.InvariantCulture)],
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
        WriteConfirmationContext context,
        ModbusWriteReceipt? receipt,
        object expectedValue,
        CancellationToken ct)
    {
        var point = context.Point;
        var timeoutMs = context.TimeoutMs;
        if (timeoutMs == 0)
        {
            return ModbusOperationResult.Success();
        }

        var started = Stopwatch.GetTimestamp();

        while (Stopwatch.GetElapsedTime(started).TotalMilliseconds <= timeoutMs)
        {
            ct.ThrowIfCancellationRequested();

            lock (_sync)
            {
                if (Stopwatch.GetElapsedTime(started).TotalMilliseconds > timeoutMs)
                    break;
                var runtimeOptions = _runtimeService.CurrentOptions;
                var endpoint = context.Role == ModbusRuntimeRole.Client ? runtimeOptions.Client : runtimeOptions.Server;
                if (_dataGeneration != context.Generation
                    || _sourceGenerations.GetValueOrDefault(context.Role) != context.SourceGeneration
                    || !ReferenceEquals(context.RuntimeOptions, runtimeOptions)
                    || !SameEndpoint(context.Endpoint, endpoint)
                    || RuntimeRole(WriteRole(ResolveWritableRole())) != context.Role
                    || !_dataMap.TryGetValue(point.Name, out var activePoint) || !point.HasSameMapping(activePoint)
                    || (receipt is { } sent && (sent.Role != context.Role
                        || (context.PreviousSourceSessionId != Guid.Empty && sent.SourceSessionId != context.PreviousSourceSessionId)
                        || (_sourceSessionIds.TryGetValue(context.Role, out var sourceId) && sourceId != sent.SourceSessionId))))
                {
                    return ModbusOperationResult.Failure("ModbusWriteConfirmationContextChanged",
                        $"The Modbus role, endpoint, session or mapping changed while confirming '{point.Name}'.");
                }

                // A changed-value event is deliberately unnecessary: every successful read
                // cycle is observable here, including equal consecutive values.
                if (receipt is { Sequence: > 0, SourceSessionId: var session } barrier && session != Guid.Empty
                    && _readObservations.TryGetValue(context.Role, out var observation)
                    && observation.Generation == context.Generation
                    && observation.Source.SourceSessionId == session
                    && observation.Source.ReadSequence > barrier.Sequence)
                {
                    var read = observation.Source.GetReadState(point.Area);
                    if (read.IsGood && read.SuccessfulReadSequence > barrier.Sequence
                        && read.SuccessfulReadSequence > context.PreviousReadSequence
                        && observation.Values.TryGetValue(point.Name, out var currentValue)
                        && ValuesEqual(currentValue.Value, expectedValue))
                        return ModbusOperationResult.Success();
                }
            }

            var remaining = timeoutMs - Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (remaining > 0)
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(50, remaining)), ct);
        }

        return ModbusOperationResult.Failure(
            "ModbusWriteConfirmationTimeout",
            $"Write to Modbus data point '{point.Name}' was not confirmed by the next snapshot.");
    }

    private static bool SameEndpoint(ModbusEndpointOptions left, ModbusEndpointOptions right) =>
        left.Host == right.Host && left.BindAddress == right.BindAddress && left.Port == right.Port
        && left.UnitId == right.UnitId && left.Enabled == right.Enabled
        && left.CoilsEnabled == right.CoilsEnabled && left.HoldingRegistersEnabled == right.HoldingRegistersEnabled
        && left.CoilStartAddress == right.CoilStartAddress && left.HoldingRegisterStartAddress == right.HoldingRegisterStartAddress
        && left.CoilCount == right.CoilCount && left.RegisterCount == right.RegisterCount
        && left.PollIntervalMs == right.PollIntervalMs;

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

    private static ModbusRunMode WriteRole(ModbusRunMode role)
        => role == ModbusRunMode.Both ? ModbusRunMode.Client : role;

    private static ModbusRuntimeRole RuntimeRole(ModbusRunMode role) => role switch
    {
        ModbusRunMode.Client => ModbusRuntimeRole.Client,
        ModbusRunMode.Server => ModbusRuntimeRole.Server,
        _ => ModbusRuntimeRole.Runtime
    };

    /// <summary>
    /// Сохраняет декодированное значение и уведомляет подписчиков только при изменении.
    /// </summary>
    private void PublishDataValue(ModbusDataValue dataValue, long generation, ModbusSnapshot source)
    {
        List<Action<ModbusDataValue>> subscribers = [];

        lock (_sync)
        {
            if (generation != _dataGeneration || !IsAcceptedSnapshot(source) || !_dataMap.ContainsKey(dataValue.Name))
                return;

            if (_latestValues.TryGetValue(dataValue.Name, out var previous)
                && ValuesEqual(previous.Value, dataValue.Value))
            {
                _latestValues[dataValue.Name] = dataValue;
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
            if (_state.ActiveRole != state.ActiveRole
                || (_state.ClientState == ModbusConnectionState.Running && state.ClientState != ModbusConnectionState.Running)
                || (_state.ServerState == ModbusConnectionState.Running && state.ServerState != ModbusConnectionState.Running))
            {
                _dataGeneration++;
                _snapshotReadNotBefore = ModbusObservationSequence.Next();
                _latestValues.Clear();
                _readObservations.Clear();
                _sourceSessionIds.Clear();
                _holdingRegisterShadow.Clear();
                _shadowIsGood = false;
                _currentDataSnapshot = ModbusDataSnapshot.Empty with { PublicationSequence = ModbusObservationSequence.Next() };
                NotifyObservationChanged();
            }
            _state = state;
            _currentDataSnapshot = _currentDataSnapshot with { State = state };
        }

        StateChanged?.Invoke(this, state);
    }

    private void PublishDataSnapshot(
        IReadOnlyDictionary<string, ModbusDataValue> values,
        ModbusSnapshot source,
        long generation)
    {
        ModbusDataSnapshot snapshot;
        lock (_sync)
        {
            if (generation != _dataGeneration || !IsAcceptedSnapshot(source))
                return;
            snapshot = new ModbusDataSnapshot(
                values.Where(pair => _dataMap.ContainsKey(pair.Key)).ToDictionary(StringComparer.OrdinalIgnoreCase),
                source.Timestamp,
                _state)
            {
                PublicationSequence = ModbusObservationSequence.Next(),
                SourceRole = source.Role,
                SourceSessionId = source.SourceSessionId,
                CoilsReadState = source.GetReadState(ModbusDataArea.Coil),
                HoldingRegistersReadState = source.GetReadState(ModbusDataArea.HoldingRegister)
            };
            _currentDataSnapshot = snapshot;
        }

        NotifyDataSnapshot(snapshot);
    }

    // Call while holding _sync. Acquisition order is checked on entry; this also
    // prevents an older callback resuming after a newer snapshot was accepted.
    private bool IsAcceptedSnapshot(ModbusSnapshot source) =>
        _acceptedSnapshots.TryGetValue(source.Role, out var accepted) && ReferenceEquals(accepted, source);

    private void NotifyDataSnapshot(ModbusDataSnapshot snapshot)
    {
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
            ModbusValueType.UInt16 or ModbusValueType.Word => registers[0],
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
