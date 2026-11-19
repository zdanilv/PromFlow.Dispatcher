using System.Collections.Concurrent;
using System.Reactive.Subjects;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Dialogs.EquipmentCardParametersDialog;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Configurator.Desktop.Workspace.RouteMap.Settings;
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class EquipmentParameterAutoDispatcherTests
{
    private const string CardId = "card";
    private const string First = "card.first";
    private const string Second = "card.second";
    private static readonly TimeSpan BarrierTimeout = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_switch_stops_old_batch_after_its_in_flight_write(bool switchBackToModbus)
    {
        using var firstWrite = new OperationBarrier();
        using var fixture = new Fixture(
            dispatch: (request, _) => request.SignalId == First ? firstWrite.EnterAsync() : Task.CompletedTask);
        var batch = fixture.AutoDispatch.DispatchPendingAsync();
        await firstWrite.Entered;

        fixture.SwitchToMock();
        if (switchBackToModbus)
            fixture.ReconnectModbus();
        firstWrite.Release();
        await batch.WaitAsync(BarrierTimeout);

        Assert.Equal([Write(First, 11)], fixture.Modbus.Requests);
        Assert.Empty(fixture.Mock.Requests);
        Assert.Equal([new Completion(CardId, First, "11", null)], fixture.Store.Completions);
        AssertState(fixture, First, "11", pending: false);
        AssertState(fixture, Second, "22", pending: true);
    }

    [Fact]
    public async Task Editing_values_during_an_attempt_protects_new_values_from_the_old_batch()
    {
        using var firstWrite = new OperationBarrier();
        using var fixture = new Fixture(
            dispatch: (request, _) => request == Write(First, 11) ? firstWrite.EnterAsync() : Task.CompletedTask);
        var batch = fixture.AutoDispatch.DispatchPendingAsync();
        await firstWrite.Entered;

        await fixture.Store.SaveAsync(CardId, [new(First, "12"), new(Second, "23")], pendingAutoDispatch: true);
        firstWrite.Fail(new TimeoutException("Old write timed out"));
        await batch.WaitAsync(BarrierTimeout);

        Assert.Equal([Write(First, 11)], fixture.Modbus.Requests);
        Assert.Equal([new Completion(CardId, First, "11", "Old write timed out")], fixture.Store.Completions);
        AssertState(fixture, First, "12", pending: true);
        AssertState(fixture, Second, "23", pending: true);

        await fixture.AutoDispatch.DispatchPendingAsync();

        Assert.Equal([Write(First, 11), Write(First, 12), Write(Second, 23)], fixture.Modbus.Requests);
        Assert.Empty(fixture.Mock.Requests);
        Assert.Equal(
            [new Completion(CardId, First, "11", "Old write timed out"), new(CardId, First, "12", null),
                new(CardId, Second, "23", null)],
            fixture.Store.Completions);
        AssertState(fixture, First, "12", pending: false);
        AssertState(fixture, Second, "23", pending: false);
    }

    [Fact]
    public async Task Timeout_after_dispatch_starts_records_the_error_and_is_never_retried()
    {
        using var firstWrite = new OperationBarrier();
        using var fixture = new Fixture(
            dispatch: (request, _) => request.SignalId == First ? firstWrite.EnterAsync() : Task.CompletedTask);
        var batch = fixture.AutoDispatch.DispatchPendingAsync();
        await firstWrite.Entered;

        firstWrite.Fail(new TimeoutException("Modbus write timed out"));
        await batch.WaitAsync(BarrierTimeout);
        fixture.SwitchToMock();
        fixture.ReconnectModbus();
        await fixture.AutoDispatch.DispatchPendingAsync();

        Assert.Equal([Write(First, 11), Write(Second, 22)], fixture.Modbus.Requests);
        Assert.Empty(fixture.Mock.Requests);
        Assert.Equal([new Completion(CardId, First, "11", "Modbus write timed out"), new(CardId, Second, "22", null)],
            fixture.Store.Completions);
        AssertState(fixture, First, "11", pending: false, "Modbus write timed out");
        AssertState(fixture, Second, "22", pending: false);
    }

    [Fact]
    public async Task Cancellation_after_dispatch_starts_consumes_only_that_attempt()
    {
        using var cancellation = new CancellationTokenSource();
        using var firstWrite = new OperationBarrier();
        using var fixture = new Fixture(dispatch: (request, token) =>
            request.SignalId == First ? firstWrite.EnterAsync().WaitAsync(token) : Task.CompletedTask);
        var batch = fixture.AutoDispatch.DispatchPendingAsync(cancellation.Token);
        await firstWrite.Entered;

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => batch.WaitAsync(BarrierTimeout));

        Assert.Equal([Write(First, 11)], fixture.Modbus.Requests);
        var firstCompletion = Assert.Single(fixture.Store.Completions);
        Assert.False(string.IsNullOrWhiteSpace(firstCompletion.Error));
        Assert.Equal(new Completion(CardId, First, "11", firstCompletion.Error), firstCompletion);
        AssertState(fixture, First, "11", pending: false, firstCompletion.Error);
        AssertState(fixture, Second, "22", pending: true);

        fixture.SwitchToMock();
        fixture.ReconnectModbus();
        await fixture.AutoDispatch.DispatchPendingAsync();

        Assert.Equal([Write(First, 11), Write(Second, 22)], fixture.Modbus.Requests);
        Assert.Empty(fixture.Mock.Requests);
        Assert.Equal([firstCompletion, new(CardId, Second, "22", null)], fixture.Store.Completions);
        AssertState(fixture, Second, "22", pending: false);
    }

    [Fact]
    public async Task Cancellation_during_validation_leaves_both_values_pending()
    {
        using var cancellation = new CancellationTokenSource();
        using var fixture = new Fixture();
        var autoDispatch = fixture.NewAutoDispatcher(new CallbackWriteValidator(_ =>
        {
            cancellation.Cancel();
            return null;
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => autoDispatch.DispatchPendingAsync(cancellation.Token));

        AssertNoAttempts(fixture);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Validation_error_after_source_switch_does_not_consume_pending(bool switchBackToModbus)
    {
        using var fixture = new Fixture();
        var autoDispatch = fixture.NewAutoDispatcher(new CallbackWriteValidator(_ =>
        {
            fixture.SwitchToMock();
            if (switchBackToModbus)
                fixture.ReconnectModbus();
            return "Invalid mapping in the old generation";
        }));

        await autoDispatch.DispatchPendingAsync();

        AssertNoAttempts(fixture);
    }

    [Fact]
    public async Task Validation_error_in_available_generation_is_recorded_once_without_sending()
    {
        const string validationError = "First parameter has no write mapping";
        using var fixture = new Fixture();
        var autoDispatch = fixture.NewAutoDispatcher(
            new CallbackWriteValidator(request => request.SignalId == First ? validationError : null));

        await autoDispatch.DispatchPendingAsync();
        fixture.SwitchToMock();
        fixture.ReconnectModbus();
        await autoDispatch.DispatchPendingAsync();

        Assert.Equal([Write(Second, 22)], fixture.Modbus.Requests);
        Assert.Empty(fixture.Mock.Requests);
        Assert.Equal([new Completion(CardId, First, "11", validationError), new(CardId, Second, "22", null)],
            fixture.Store.Completions);
        AssertState(fixture, First, "11", pending: false, validationError);
        AssertState(fixture, Second, "22", pending: false);
    }

    [Fact]
    public async Task Disposing_runtime_during_validation_leaves_both_values_pending()
    {
        using var fixture = new Fixture();
        var autoDispatch = fixture.NewAutoDispatcher(new CallbackWriteValidator(_ =>
        {
            fixture.Runtime.Dispose();
            return null;
        }));

        await autoDispatch.DispatchPendingAsync();

        AssertNoAttempts(fixture);
    }

    [Theory]
    [InlineData(null, true, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task Unavailable_Modbus_connection_leaves_values_pending(
        bool? connected, bool qualityGood, bool stale)
    {
        using var fixture = new Fixture(publishConnection: false);
        fixture.ModbusSignals.PublishConnection(connected, qualityGood, stale);

        await fixture.AutoDispatch.DispatchPendingAsync();

        AssertNoAttempts(fixture);
    }

    [Fact]
    public async Task New_Modbus_generation_requires_its_own_connection_snapshot()
    {
        using var fixture = new Fixture();
        fixture.SwitchToMock();
        fixture.Runtime.SwitchSource(RouteMapSignalSource.Modbus);

        await fixture.AutoDispatch.DispatchPendingAsync();

        AssertNoAttempts(fixture);
    }

    [Fact]
    public async Task Queued_batch_cannot_adopt_a_new_generation_while_waiting_for_the_gate()
    {
        using var firstWrite = new OperationBarrier();
        using var fixture = new Fixture(
            dispatch: (request, _) => request.SignalId == First ? firstWrite.EnterAsync() : Task.CompletedTask);
        var oldBatch = fixture.AutoDispatch.DispatchPendingAsync();
        await firstWrite.Entered;
        var queuedOldBatch = fixture.NewAutoDispatcher().DispatchPendingAsync();
        Assert.False(queuedOldBatch.IsCompleted);

        fixture.SwitchToMock();
        fixture.ReconnectModbus();
        firstWrite.Release();
        await Task.WhenAll(oldBatch, queuedOldBatch).WaitAsync(BarrierTimeout);

        Assert.Equal([Write(First, 11)], fixture.Modbus.Requests);
        Assert.Empty(fixture.Mock.Requests);
        Assert.Equal([new Completion(CardId, First, "11", null)], fixture.Store.Completions);
        AssertState(fixture, First, "11", pending: false);
        AssertState(fixture, Second, "22", pending: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_batch_waits_for_outcome_persistence_and_sends_each_value_once(
        bool switchGeneration)
    {
        using var firstWrite = new OperationBarrier();
        using var firstCompletion = new OperationBarrier();
        using var fixture = new Fixture(
            dispatch: (request, _) => request.SignalId == First ? firstWrite.EnterAsync() : Task.CompletedTask,
            persist: completion => completion.SignalId == First ? firstCompletion.EnterAsync() : Task.CompletedTask);
        var firstBatch = fixture.AutoDispatch.DispatchPendingAsync();
        await firstWrite.Entered;

        if (switchGeneration)
        {
            fixture.SwitchToMock();
            fixture.ReconnectModbus();
        }
        var concurrentBatch = fixture.NewAutoDispatcher().DispatchPendingAsync();
        Assert.False(concurrentBatch.IsCompleted);

        firstWrite.Release();
        await firstCompletion.Entered;
        Assert.False(concurrentBatch.IsCompleted);
        Assert.Equal([Write(First, 11)], fixture.Modbus.Requests);
        Assert.Empty(fixture.Store.Completions);

        firstCompletion.Release();
        await Task.WhenAll(firstBatch, concurrentBatch).WaitAsync(BarrierTimeout);

        Assert.Equal([Write(First, 11), Write(Second, 22)], fixture.Modbus.Requests);
        Assert.Empty(fixture.Mock.Requests);
        Assert.Equal([new Completion(CardId, First, "11", null), new(CardId, Second, "22", null)],
            fixture.Store.Completions);
        AssertState(fixture, First, "11", pending: false);
        AssertState(fixture, Second, "22", pending: false);
    }

    private static void AssertNoAttempts(Fixture fixture)
    {
        Assert.Empty(fixture.Modbus.Requests);
        Assert.Empty(fixture.Mock.Requests);
        Assert.Empty(fixture.Store.Completions);
        AssertState(fixture, First, "11", pending: true);
        AssertState(fixture, Second, "22", pending: true);
    }

    [Fact]
    public async Task Editing_value_while_reservation_waits_does_not_send_the_old_value()
    {
        using var reservation = new OperationBarrier();
        using var fixture = new Fixture(begin: signal => signal == First ? reservation.EnterAsync() : Task.CompletedTask);
        var batch = fixture.AutoDispatch.DispatchPendingAsync();
        await reservation.Entered;
        await fixture.Store.SaveAsync(CardId, [new(First, "12")], pendingAutoDispatch: true);
        reservation.Release();
        await batch.WaitAsync(BarrierTimeout);

        Assert.Equal([Write(Second, 22)], fixture.Modbus.Requests);
        AssertState(fixture, First, "12", pending: true);
        await fixture.NewAutoDispatcher().DispatchPendingAsync();
        Assert.Equal([Write(Second, 22), Write(First, 12)], fixture.Modbus.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_change_or_cancellation_during_reservation_defers_the_unstarted_attempt(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        using var reservation = new OperationBarrier();
        using var fixture = new Fixture(begin: signal => signal == First ? reservation.EnterAsync() : Task.CompletedTask);
        var batch = fixture.AutoDispatch.DispatchPendingAsync(cancellation.Token);
        await reservation.Entered;
        if (cancel)
            cancellation.Cancel();
        else
            fixture.SwitchToMock();
        reservation.Release();
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => batch.WaitAsync(BarrierTimeout));
        else
            await batch.WaitAsync(BarrierTimeout);

        AssertNoAttempts(fixture);
        fixture.ReconnectModbus();
        await fixture.NewAutoDispatcher().DispatchPendingAsync();
        Assert.Equal([Write(First, 11), Write(Second, 22)], fixture.Modbus.Requests);
        Assert.Empty(fixture.Mock.Requests);
    }

    private static void AssertState(
        Fixture fixture, string signalId, string savedValue, bool pending, string? error = null)
    {
        var parameter = fixture.Store.GetParameter(signalId);
        Assert.Equal((savedValue, pending, error),
            (parameter.SavedValue, parameter.PendingAutoDispatch, parameter.LastDispatchError));
    }

    private static SignalWriteRequest Write(string signalId, ushort value) =>
        new(signalId, value, SignalValueType.Word);

    private sealed class Fixture : IDisposable
    {
        private readonly ManualSignalProvider _mockSignals = new();

        public Fixture(
            Func<SignalWriteRequest, CancellationToken, Task>? dispatch = null,
            Func<Completion, Task>? persist = null,
            bool publishConnection = true,
            Func<string, Task>? begin = null)
        {
            Store = new RecordingValueStore(persist, begin);
            Modbus = new RecordingDispatcher(dispatch);
            Runtime = new RouteMapSignalRuntime(_mockSignals, Mock, ModbusSignals, Modbus, RouteMapSignalSource.Modbus);
            AutoDispatch = NewAutoDispatcher();
            if (publishConnection)
                ModbusSignals.PublishConnection(true);
        }

        public RouteMapSignalRuntime Runtime { get; }
        public EquipmentParameterAutoDispatcher AutoDispatch { get; }
        public ManualSignalProvider ModbusSignals { get; } = new();
        public RecordingDispatcher Modbus { get; }
        public RecordingDispatcher Mock { get; } = new();
        public RecordingValueStore Store { get; }

        public EquipmentParameterAutoDispatcher NewAutoDispatcher(IEquipmentParameterWriteValidator? validator = null) =>
            new(Store, Runtime, validator ?? NoopEquipmentParameterWriteValidator.Instance, Runtime);

        public void SwitchToMock()
        {
            Runtime.SwitchSource(RouteMapSignalSource.Mock);
            _mockSignals.PublishConnection(true);
        }

        public void ReconnectModbus()
        {
            Runtime.SwitchSource(RouteMapSignalSource.Modbus);
            ModbusSignals.PublishConnection(true);
        }

        public void Dispose() => Runtime.Dispose();
    }

    private sealed class ManualSignalProvider : ISignalValueProvider, IDisposable
    {
        private readonly Subject<IReadOnlyDictionary<string, SignalValue>> _snapshots = new();

        public IObservable<IReadOnlyDictionary<string, SignalValue>> Observe() => _snapshots;

        public void PublishConnection(bool? connected, bool qualityGood = true, bool stale = false)
        {
            var snapshot = new Dictionary<string, SignalValue>();
            if (connected is { } value)
            {
                const string signalId = RouteMapSystemSignalIds.ConnectionConnected;
                snapshot[signalId] = new SignalValue(
                    signalId, value, SignalValueType.Bool, DateTimeOffset.UnixEpoch, qualityGood, stale);
            }
            _snapshots.OnNext(snapshot);
        }

        public void Dispose() => _snapshots.Dispose();
    }

    private sealed class OperationBarrier : IDisposable
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task.WaitAsync(BarrierTimeout);

        public Task EnterAsync()
        {
            _entered.TrySetResult();
            return _release.Task;
        }

        public void Release() => _release.TrySetResult();

        public void Fail(Exception exception) => _release.SetException(exception);

        public void Dispose() => Release();
    }

    private sealed class RecordingDispatcher(Func<SignalWriteRequest, CancellationToken, Task>? dispatch = null)
        : IEquipmentCommandDispatcher
    {
        private readonly ConcurrentQueue<SignalWriteRequest> _requests = new();

        public SignalWriteRequest[] Requests => _requests.ToArray();

        public Task DispatchAsync(SignalWriteRequest request, CancellationToken cancellationToken = default)
        {
            _requests.Enqueue(request);
            return dispatch?.Invoke(request, cancellationToken) ?? Task.CompletedTask;
        }
    }

    private sealed class CallbackWriteValidator(Func<SignalWriteRequest, string?> callback)
        : IEquipmentParameterWriteValidator
    {
        public string? Validate(SignalWriteRequest request) => callback(request);
    }

    private sealed record Completion(string CardId, string SignalId, string ExpectedValue, string? Error);

    private sealed class RecordingValueStore(Func<Completion, Task>? persist, Func<string, Task>? begin) : IEquipmentParameterValueStore
    {
        private readonly object _sync = new();
        private readonly List<Completion> _completions = [];
        private readonly List<EquipmentCardParameter> _parameters = [Parameter(First, "11"), Parameter(Second, "22")];

        public Completion[] Completions
        {
            get
            {
                lock (_sync)
                    return _completions.ToArray();
            }
        }

        public EquipmentCardParameter GetParameter(string signalId)
        {
            lock (_sync)
                return _parameters.Single(parameter => parameter.Binding.SignalId == signalId);
        }

        public Task SaveAsync(
            string cardId, IReadOnlyCollection<EquipmentParameterSetpoint> values, bool pendingAutoDispatch,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(CardId, cardId);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                foreach (var value in values)
                {
                    var index = FindParameterIndex(value.SignalId);
                    _parameters[index] = _parameters[index] with
                    {
                        SavedValue = value.Value,
                        PendingAutoDispatch = pendingAutoDispatch,
                        LastDispatchError = null
                    };
                }
            }
            return Task.CompletedTask;
        }

        public IReadOnlyList<PendingEquipmentParameter> GetPending()
        {
            lock (_sync)
            {
                return _parameters.Where(parameter => parameter.PendingAutoDispatch)
                    .Select(parameter => new PendingEquipmentParameter(CardId, parameter))
                    .ToArray();
            }
        }

        public async Task<bool> TryBeginDispatchAsync(string cardId, string signalId, string expectedSavedValue,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (begin is not null)
                await begin(signalId);
            lock (_sync)
            {
                var index = FindParameterIndex(signalId);
                var parameter = _parameters[index];
                if (!parameter.PendingAutoDispatch || parameter.SavedValue != expectedSavedValue)
                    return false;
                _parameters[index] = parameter with { PendingAutoDispatch = false, LastDispatchError = "Unknown outcome" };
                return true;
            }
        }

        public Task DeferDispatchAsync(string cardId, string signalId, string expectedSavedValue,
            CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                var index = FindParameterIndex(signalId);
                if (_parameters[index].SavedValue == expectedSavedValue)
                    _parameters[index] = _parameters[index] with { PendingAutoDispatch = true, LastDispatchError = null };
            }
            return Task.CompletedTask;
        }

        public async Task CompleteDispatchAsync(
            string cardId, string signalId, string expectedSavedValue, string? errorMessage,
            CancellationToken cancellationToken = default)
        {
            var completion = new Completion(cardId, signalId, expectedSavedValue, errorMessage);
            if (persist is not null)
                await persist(completion);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                _completions.Add(completion);
                var index = FindParameterIndex(signalId);
                var parameter = _parameters[index];
                if (parameter.SavedValue == expectedSavedValue)
                    _parameters[index] = parameter with { PendingAutoDispatch = false, LastDispatchError = errorMessage };
            }
        }

        private int FindParameterIndex(string signalId) =>
            _parameters.FindIndex(parameter => parameter.Binding.SignalId == signalId);

        private static EquipmentCardParameter Parameter(string signalId, string value) => new(
            signalId,
            new SignalBinding(SignalBindingRole.EquipmentParameter, signalId, SignalBindingDirection.Write,
                SignalValueType.Word),
            SavedValue: value,
            PendingAutoDispatch: true);
    }
}
