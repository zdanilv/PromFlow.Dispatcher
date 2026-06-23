using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Signals;
using Configurator.Infrastructure.Persistence.Archive;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Archive;

public sealed class CommandAuditServiceTests
{
    [Fact]
    public async Task RecordCommandAsync_EnqueuesCriticalEquipmentCommandEnvelope()
    {
        var ingestor = new RecordingIngestor();
        var service = new CommandAuditService(ingestor);
        var record = CreateCommandRecord(EquipmentCommandAuditResult.Requested);

        var result = await service.RecordCommandAsync(record, CancellationToken.None);

        Assert.True(result.Succeeded);
        var envelope = Assert.Single(ingestor.Envelopes);
        Assert.Equal(ArchiveRecordKind.EquipmentCommandAudit, envelope.Kind);
        Assert.Equal(ArchivePriority.Critical, envelope.Priority);
        Assert.Same(record, envelope.Record);
    }

    [Fact]
    public async Task RecordPhysicalWriteAsync_PropagatesIngestorFailure()
    {
        var ingestor = new RecordingIngestor
        {
            NextResult = ArchiveOperationResult.Failure("ArchiveQueueStopped", "Archive queue is stopped.")
        };
        var service = new CommandAuditService(ingestor);
        var record = new PhysicalModbusWriteAuditRecord(
            Guid.NewGuid(),
            commandId: Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            completedAtUtc: DateTimeOffset.UtcNow,
            ModbusRuntimeRole.Client,
            ModbusDataArea.HoldingRegister,
            address: 400,
            quantity: 1,
            payloadBlob: [0x12, 0x34],
            succeeded: true,
            errorCode: null,
            errorMessage: null,
            schemaVersion: 1);

        var result = await service.RecordPhysicalWriteAsync(record, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("ArchiveQueueStopped", result.ErrorCode);
        var envelope = Assert.Single(ingestor.Envelopes);
        Assert.Equal(ArchiveRecordKind.PhysicalModbusWriteAudit, envelope.Kind);
        Assert.Equal(ArchivePriority.Critical, envelope.Priority);
        Assert.Same(record, envelope.Record);
    }

    private static EquipmentCommandAuditRecord CreateCommandRecord(EquipmentCommandAuditResult result)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            completedAtUtc: result == EquipmentCommandAuditResult.Requested ? null : DateTimeOffset.UtcNow,
            sessionId: null,
            userId: null,
            username: null,
            "device-1",
            "system.emergency",
            SignalValueType.Bool,
            "true",
            writeMode: null,
            result,
            errorCode: null,
            errorMessage: null,
            CommandConfirmationStatus.Pending,
            confirmedAtUtc: null,
            schemaVersion: 1);

    private sealed class RecordingIngestor : IArchiveIngestor
    {
        public List<ArchiveEnvelope> Envelopes { get; } = [];

        public ArchiveOperationResult NextResult { get; set; } = ArchiveOperationResult.Success();

        public ValueTask<ArchiveOperationResult> EnqueueAsync(
            ArchiveEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Envelopes.Add(envelope);
            return ValueTask.FromResult(NextResult);
        }
    }
}
