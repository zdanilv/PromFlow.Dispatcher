using System.Reactive.Subjects;
using Configurator.Application.Services.History;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class RouteMapSessionJournalTests
{
    [Fact]
    public void RecordSignalSent_StoresSignalMetadataAndModbusAddress()
    {
        var journal = new RouteMapSessionJournal();
        var definition = RouteMapSeed.Create();
        var options = new ModbusOptions
        {
            DataMap =
            [
                new()
                {
                    Name = "equip.bucket.start",
                    Area = ModbusDataArea.HoldingRegister,
                    Address = 3,
                    BitIndex = 2,
                    Type = ModbusValueType.Bool,
                    Access = ModbusDataAccess.ReadWrite
                }
            ]
        };

        journal.RecordSignalSent(
            new SignalWriteRequest("equip.bucket.start", true, SignalValueType.Bool),
            definition,
            options,
            DateTimeOffset.UtcNow);

        var item = Assert.Single(journal.History);
        Assert.Equal("Отправлено", item.EventText);
        Assert.Equal("equip.bucket.start", item.Name);
        Assert.Contains(nameof(SignalBindingRole.StartCommand), item.Roles);
        Assert.Contains("Карточка", item.Objects);
        Assert.Equal("3", item.Address);
        Assert.Equal("2", item.Bit);
        Assert.Equal("true", item.Value);
        Assert.Equal("↑", item.DirectionGlyph);
    }

    [Fact]
    public void RecordSignalSnapshot_StoresFirstQualityValueAndOnlyChangedValues()
    {
        var journal = new RouteMapSessionJournal();
        var definition = RouteMapSeed.Create();
        var options = new ModbusOptions
        {
            DataMap =
            [
                new()
                {
                    Name = "route.node.bsu_1.active",
                    Area = ModbusDataArea.Coil,
                    Address = 5,
                    Type = ModbusValueType.Bool,
                    Access = ModbusDataAccess.Read
                },
                new()
                {
                    Name = RouteMapSystemSignalIds.ConnectionConnected,
                    Area = ModbusDataArea.Coil,
                    Address = 6,
                    Type = ModbusValueType.Bool,
                    Access = ModbusDataAccess.Read
                }
            ]
        };
        var now = DateTimeOffset.UtcNow;

        journal.RecordSignalSnapshot(CreateSnapshot("route.node.bsu_1.active", true, now), definition, options);
        journal.RecordSignalSnapshot(CreateSnapshot("route.node.bsu_1.active", true, now.AddSeconds(1)), definition, options);
        journal.RecordSignalSnapshot(CreateSnapshot("route.node.bsu_1.active", false, now.AddSeconds(2)), definition, options);

        Assert.Equal(2, journal.History.Count);
        Assert.All(journal.History, item => Assert.Equal("Получено", item.EventText));
        Assert.All(journal.History, item => Assert.Equal("↓", item.DirectionGlyph));
        Assert.Equal(["true", "false"], journal.History.Select(item => item.Value).ToArray());
        Assert.DoesNotContain(journal.History, item => item.Name == RouteMapSystemSignalIds.ConnectionConnected);
    }

    [Fact]
    public void ClearDismissibleNotifications_RemovesOnlyInactiveNotifications()
    {
        var journal = new RouteMapSessionJournal();
        var active = CreateAlarm("alarm.active", ModbusDataArea.Coil, 0);
        var closed = CreateAlarm("alarm.closed", ModbusDataArea.HoldingRegister, 5, bitIndex: 2);
        journal.ShowAlarmNotification(active, DateTimeOffset.UtcNow, markUnread: true);
        journal.ShowAlarmNotification(closed, DateTimeOffset.UtcNow, markUnread: true);
        journal.RecordAlarmCleared(closed, DateTimeOffset.UtcNow);

        var removed = journal.ClearDismissibleNotifications();

        Assert.Equal(1, removed);
        var notification = Assert.Single(journal.Notifications);
        Assert.Equal("alarm.active", notification.Id);
        Assert.True(notification.IsActive);
        var clearEvent = Assert.Single(journal.History, item => item.EventText == "Снято");
        Assert.Equal("5", clearEvent.Address);
        Assert.Equal("2", clearEvent.Bit);
        Assert.Equal("↓", clearEvent.DirectionGlyph);
    }

    [Fact]
    public void AlarmAcknowledgement_UsesOutgoingDirection()
    {
        var journal = new RouteMapSessionJournal();
        var alarm = CreateAlarm("alarm.main", ModbusDataArea.HoldingRegister, 7, bitIndex: 4);

        journal.RecordAlarmAcknowledged(alarm, DateTimeOffset.UtcNow);

        var item = Assert.Single(journal.History);
        Assert.Equal("OK", item.EventText);
        Assert.Equal("↑", item.DirectionGlyph);
        Assert.Equal("7", item.Address);
        Assert.Equal("4", item.Bit);
    }

    [Fact]
    public void Sink_receives_every_event_with_source_address_and_original_utc()
    {
        var sink = new RecordingHistorySink();
        var journal = new RouteMapSessionJournal(sink);
        var management = RouteMapSeed.Create();
        var timestamp = new DateTimeOffset(2026, 11, 23, 10, 30, 0, TimeSpan.FromHours(3));
        var alarm = CreateAlarm("alarm.test", ModbusDataArea.HoldingRegister, 42, bitIndex: 4);
        var options = new ModbusOptions
        {
            DataMap = [new() { Name = "test", Area = ModbusDataArea.Coil, Address = 8, Access = ModbusDataAccess.Read }]
        };

        journal.RecordSignalSnapshot(CreateSnapshot("test", true, timestamp), management, options, RouteMapSignalSource.Modbus);
        journal.RecordSignalSent(new("unmapped.command", true, SignalValueType.Bool), management, options, timestamp, RouteMapSignalSource.Mock);
        journal.RecordAlarmActivated(alarm, timestamp);
        journal.RecordAlarmAcknowledged(alarm, timestamp);
        journal.RecordAlarmCleared(alarm, timestamp);

        Assert.Equal(["Получено", "Отправлено", "Тревога", "OK", "Снято"], sink.Events.Select(item => item.EventType));
        Assert.Equal(["Modbus", "Mock", "Modbus", "Modbus", "Modbus"], sink.Events.Select(item => item.Source));
        Assert.All(sink.Events, item =>
        {
            Assert.Equal(timestamp.ToUniversalTime(), item.TimestampUtc);
            Assert.Equal(TimeSpan.Zero, item.TimestampUtc.Offset);
            Assert.Equal(TimeSpan.Zero, item.RecordedUtc.Offset);
            Assert.NotEqual(Guid.Empty, item.EventUid);
        });
        Assert.Single(sink.Events.Select(x => x.SessionId).Distinct());
        Assert.Equal(5, sink.Events.Select(x => x.EventUid).Distinct().Count());
        Assert.Equal("Coil", sink.Events[0].ModbusArea);
        Assert.Equal(8, sink.Events[0].Address);
        Assert.Null(sink.Events[1].Address);
        Assert.Null(sink.Events[1].ModbusArea);
        Assert.Equal(4, sink.Events[2].BitIndex);
    }

    [Fact]
    public void History_is_bounded_and_sink_still_receives_all_concurrent_events_in_display_order()
    {
        var sink = new RecordingHistorySink();
        var journal = new RouteMapSessionJournal(sink);
        var start = DateTimeOffset.UtcNow;
        Parallel.For(0, 3000, index => journal.RecordAlarmActivated(
            CreateAlarm($"alarm.{index}", ModbusDataArea.Coil, index), start));

        Assert.Equal(3000, sink.Events.Count);
        Assert.Equal(RouteMapSessionJournal.HistoryCapacity, journal.History.Count);
        Assert.Equal(sink.Events.TakeLast(RouteMapSessionJournal.HistoryCapacity).Select(item => item.Name),
            journal.SnapshotHistory().Select(item => item.Name));
    }

    [Fact]
    public void Collector_needs_no_view_and_subscribes_once_across_source_generations()
    {
        var profilePath = Path.Combine(Path.GetTempPath(), $"promflow-history-collector-{Guid.NewGuid():N}.json");
        var seed = RouteMapSeed.Create();
        var mapper = new RouteMapConfigurationMapper(seed);
        using var configuration = new RouteMapConfigurationManager(new RouteMapConfigurationStorage(profilePath),
            mapper, new RouteMapConfigurationValidator(), new RouteMapConfigurationMigrator());
        var monitor = new HistoryOptionsMonitor(new ModbusOptions
        {
            DataMap = [new() { Name = "read", Address = 13, Type = ModbusValueType.Bool, Access = ModbusDataAccess.Read }]
        });
        var mock = new HistorySignalProvider();
        var modbus = new HistorySignalProvider();
        var command = new UnusedDispatcher();
        using var runtime = new RouteMapSignalRuntime(mock, command, modbus, command, RouteMapSignalSource.Mock);
        var sink = new RecordingHistorySink();
        var journal = new RouteMapSessionJournal(sink);
        using var collector = new RouteMapHistoryCollector(runtime, journal, configuration, monitor);
        var now = DateTimeOffset.UtcNow;
        mock.Updates.OnNext(CreateSnapshot("read", true, now));
        collector.Start();
        collector.Start();
        mock.Updates.OnNext(CreateSnapshot("read", true, now));
        runtime.SwitchSource(RouteMapSignalSource.Modbus);
        mock.Updates.OnNext(CreateSnapshot("read", false, now));
        var stale = CreateSnapshot("read", false, now);
        modbus.Updates.OnNext(new Dictionary<string, SignalValue> { ["read"] = stale["read"] with { IsStale = true } });
        modbus.Updates.OnNext(new Dictionary<string, SignalValue> { ["read"] = stale["read"] with { IsQualityGood = false } });
        modbus.Updates.OnNext(CreateSnapshot("read", true, now));
        modbus.Updates.OnNext(CreateSnapshot("read", true, now));
        modbus.Updates.OnNext(CreateSnapshot("read", false, now));
        collector.Dispose();
        modbus.Updates.OnNext(CreateSnapshot("read", true, now));

        Assert.Equal(["Mock", "Modbus", "Modbus"], sink.Events.Select(item => item.Source));
        Assert.Equal(["true", "true", "false"], sink.Events.Select(item => item.Value));
    }

    [Fact]
    public async Task Shutdown_waits_for_accepted_dispatch_to_record_its_result()
    {
        var profilePath = Path.Combine(Path.GetTempPath(), $"promflow-history-drain-{Guid.NewGuid():N}.json");
        using var configuration = new RouteMapConfigurationManager(new RouteMapConfigurationStorage(profilePath),
            new RouteMapConfigurationMapper(RouteMapSeed.Create()), new RouteMapConfigurationValidator(), new RouteMapConfigurationMigrator());
        var sink = new RecordingHistorySink();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new GatedDispatcher(completion.Task);
        using var runtime = new RouteMapSignalRuntime(new HistorySignalProvider(), dispatcher, new HistorySignalProvider(),
            new UnusedDispatcher(), RouteMapSignalSource.Mock, new RouteMapSessionJournal(sink), configuration,
            new HistoryOptionsMonitor(new ModbusOptions()));

        var send = runtime.DispatchAsync(new("test.command", true, SignalValueType.Bool));
        runtime.Dispose();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var drain = runtime.WaitForPendingDispatchesAsync(timeout.Token);
        Assert.False(drain.IsCompleted);
        Assert.Empty(sink.Events);

        completion.SetResult();
        await drain;
        await send;
        var entry = Assert.Single(sink.Events);
        Assert.Equal("Отправлено", entry.EventType);
        Assert.Equal("Mock", entry.Source);
    }

    private sealed class RecordingHistorySink : IHistoryEventSink
    {
        public List<HistoryEvent> Events { get; } = [];
        public bool TryWrite(HistoryEvent historyEvent)
        {
            Events.Add(historyEvent);
            return true;
        }
    }

    private sealed class HistoryOptionsMonitor(ModbusOptions options) : IOptionsMonitor<ModbusOptions>
    {
        public ModbusOptions CurrentValue => options;
        public ModbusOptions Get(string? name) => options;
        public IDisposable? OnChange(Action<ModbusOptions, string?> listener) => null;
    }

    private sealed class HistorySignalProvider : ISignalValueProvider, IDisposable
    {
        public Subject<IReadOnlyDictionary<string, SignalValue>> Updates { get; } = new();
        public IObservable<IReadOnlyDictionary<string, SignalValue>> Observe() => Updates;
        public void Dispose() => Updates.Dispose();
    }

    private sealed class UnusedDispatcher : IEquipmentCommandDispatcher
    {
        public Task DispatchAsync(SignalWriteRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("History collection dispatched a command.");
    }

    private sealed class GatedDispatcher(Task completion) : IEquipmentCommandDispatcher
    {
        public Task DispatchAsync(SignalWriteRequest request, CancellationToken cancellationToken = default) => completion;
    }

    private static IReadOnlyDictionary<string, SignalValue> CreateSnapshot(
        string signalId,
        bool value,
        DateTimeOffset timestamp)
        => new Dictionary<string, SignalValue>(StringComparer.OrdinalIgnoreCase)
        {
            [signalId] = new(signalId, value, SignalValueType.Bool, timestamp, IsQualityGood: true, IsStale: false),
            [RouteMapSystemSignalIds.ConnectionConnected] = new(
                RouteMapSystemSignalIds.ConnectionConnected,
                true,
                SignalValueType.Bool,
                timestamp,
                IsQualityGood: true,
            IsStale: false)
        };

    private static ModbusAlarmOptions CreateAlarm(
        string id,
        ModbusDataArea area,
        int address,
        int? bitIndex = null)
        => new()
        {
            Id = id,
            Kind = ModbusAlarmKind.Fault,
            Message = id,
            Alarm = new ModbusBitAddressOptions
            {
                Area = area,
                Address = address,
                BitIndex = bitIndex
            },
            Acknowledgement = new ModbusBitAddressOptions
            {
                Area = area,
                Address = address,
                BitIndex = bitIndex
            }
        };
}
