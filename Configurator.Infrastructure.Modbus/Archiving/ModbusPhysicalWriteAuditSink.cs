using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Configurator.Infrastructure.Modbus.Archiving;

public sealed class ModbusPhysicalWriteAuditSink(
    ICommandAuditService commandAuditService,
    IOptionsMonitor<ArchiveOptions> archiveOptions,
    ILogger<ModbusPhysicalWriteAuditSink> logger)
{
    private const int SchemaVersion = 1;

    public async Task RecordAsync(
        CommandExecutionContext? context,
        ModbusRuntimeRole role,
        ModbusDataArea area,
        int address,
        int quantity,
        IReadOnlyList<byte> payloadBlob,
        DateTimeOffset attemptedAtUtc,
        DateTimeOffset? completedAtUtc,
        bool succeeded,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken = default)
    {
        if (context is null)
        {
            return;
        }

        var record = new PhysicalModbusWriteAuditRecord(
            Guid.NewGuid(),
            context.CommandId,
            attemptedAtUtc,
            completedAtUtc,
            role,
            area,
            address,
            quantity,
            payloadBlob,
            succeeded,
            errorCode,
            errorMessage,
            SchemaVersion);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1, archiveOptions.CurrentValue.CommandAuditEnqueueTimeoutMs)));

        try
        {
            var result = await commandAuditService
                .RecordPhysicalWriteAsync(record, timeout.Token)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                logger.LogWarning(
                    "Physical Modbus write audit enqueue failed for {CommandId}: {ErrorCode} {ErrorMessage}",
                    context.CommandId,
                    result.ErrorCode,
                    result.ErrorMessage);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Physical Modbus write audit enqueue timed out for {CommandId}",
                context.CommandId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Physical Modbus write audit enqueue threw for {CommandId}", context.CommandId);
        }
    }

    public static byte[] BuildCoilPayload(bool value)
        => [value ? (byte)1 : (byte)0];

    public static byte[] BuildRegisterPayload(IReadOnlyList<ushort> registers)
    {
        ArgumentNullException.ThrowIfNull(registers);

        var payload = new byte[checked(registers.Count * 2)];
        for (var index = 0; index < registers.Count; index++)
        {
            payload[index * 2] = (byte)(registers[index] >> 8);
            payload[(index * 2) + 1] = (byte)(registers[index] & 0xFF);
        }

        return payload;
    }
}
