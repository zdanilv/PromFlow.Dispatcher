using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Services;
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
