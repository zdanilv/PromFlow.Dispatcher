using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Signals;
using Xunit;

namespace Configurator.Tests.Unit.Archiving;

public sealed class ArchiveRecordContractTests
{
    [Fact]
    public void RawSnapshot_NormalizesUtcAndDefensivelyCopiesArrays()
    {
        var coils = new[] { true, false };
        var registers = new ushort[] { 10, 20 };
        var captured = new DateTimeOffset(2026, 6, 22, 10, 30, 0, TimeSpan.FromHours(3));

        var record = new RawModbusSnapshotArchiveRecord(
            Guid.NewGuid(),
            "device-01",
            ModbusRuntimeRole.Client,
            sequenceNumber: 42,
            captured,
            coilStartAddress: 100,
            holdingRegisterStartAddress: 400,
            coils,
            registers,
            configurationHash: "abc123",
            ArchiveResolution.HighResolution,
            schemaVersion: 1);

        coils[0] = false;
        registers[0] = 99;

        Assert.Equal(TimeSpan.Zero, record.CapturedAtUtc.Offset);
        Assert.Equal(new DateTimeOffset(2026, 6, 22, 7, 30, 0, TimeSpan.Zero), record.CapturedAtUtc);
        Assert.True(record.Coils[0]);
        Assert.Equal((ushort)10, record.HoldingRegisters[0]);
    }

    [Fact]
    public void RawSnapshot_InvalidConstructorArguments_Throw()
    {
        var exception = Assert.Throws<ArgumentException>(() => new RawModbusSnapshotArchiveRecord(
            Guid.Empty,
            "device-01",
            ModbusRuntimeRole.Client,
            sequenceNumber: 0,
            DateTimeOffset.UtcNow,
            coilStartAddress: 0,
            holdingRegisterStartAddress: 0,
            Array.Empty<bool>(),
            Array.Empty<ushort>(),
            configurationHash: "hash",
            ArchiveResolution.HighResolution,
            schemaVersion: 1));

        Assert.Equal("id", exception.ParamName);
    }

    [Fact]
    public void PhysicalWrite_DefensivelyCopiesPayloadBlob()
    {
        var payload = new byte[] { 1, 2, 3 };
        var attempted = new DateTimeOffset(2026, 6, 22, 12, 0, 0, TimeSpan.FromHours(3));

        var record = new PhysicalModbusWriteAuditRecord(
            Guid.NewGuid(),
            commandId: Guid.NewGuid(),
            attempted,
            completedAtUtc: attempted.AddMilliseconds(5),
            ModbusRuntimeRole.Server,
            ModbusDataArea.HoldingRegister,
            address: 7,
            quantity: 2,
            payload,
            succeeded: true,
            errorCode: null,
            errorMessage: null,
            schemaVersion: 1);

        payload[0] = 9;

        Assert.Equal(TimeSpan.Zero, record.AttemptedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, record.CompletedAtUtc!.Value.Offset);
        Assert.Equal((byte)1, record.PayloadBlob[0]);
    }

    [Fact]
    public void ArchivePage_DefensivelyCopiesItems()
    {
        var items = new List<string> { "first" };

        var page = new ArchivePage<string>(
            items,
            pageNumber: 1,
            pageSize: 10,
            totalCount: 1,
            hasMore: false);

        items[0] = "changed";

        Assert.Equal("first", page.Items[0]);
        Assert.Equal(1, page.PageNumber);
        Assert.False(page.HasMore);
    }

    [Fact]
    public void AuditRecords_NormalizeUtcTimestamps()
    {
        var local = new DateTimeOffset(2026, 6, 22, 15, 0, 0, TimeSpan.FromHours(3));

        var command = new EquipmentCommandAuditRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            requestedAtUtc: local,
            completedAtUtc: local.AddSeconds(1),
            sessionId: "session-1",
            userId: "user-1",
            username: "operator",
            deviceId: "device-01",
            signalId: "system.emergency",
            SignalValueType.Bool,
            requestedValueCanonical: "true",
            ModbusWriteMode.Latched,
            EquipmentCommandAuditResult.Succeeded,
            errorCode: null,
            errorMessage: null,
            CommandConfirmationStatus.Confirmed,
            confirmedAtUtc: local.AddSeconds(2),
            schemaVersion: 1);
        var status = new ModbusStatusArchiveRecord(
            Guid.NewGuid(),
            "device-01",
            occurredAtUtc: local,
            ModbusConnectionState.Running,
            ModbusConnectionState.Stopped,
            clientMessage: "Connected",
            serverMessage: "Stopped",
            lastError: null,
            schemaVersion: 1);
        var security = new SecurityAuditRecord(
            Guid.NewGuid(),
            occurredAtUtc: local,
            eventType: "Login",
            SecurityAuditSeverity.Information,
            actorUserId: "user-1",
            actorUsername: "operator",
            sessionId: "session-1",
            targetUserId: null,
            SecurityAuditResult.Succeeded,
            reasonCode: null,
            detailsJson: "{\"reason\":\"test\"}",
            schemaVersion: 1);

        Assert.Equal(TimeSpan.Zero, command.RequestedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, command.CompletedAtUtc!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, command.ConfirmedAtUtc!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, status.OccurredAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, security.OccurredAtUtc.Offset);
    }
}
