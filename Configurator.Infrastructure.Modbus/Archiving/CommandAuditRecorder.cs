using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Signals;
using Configurator.Infrastructure.Modbus.RouteMap;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Configurator.Infrastructure.Modbus.Archiving;

public sealed class CommandAuditRecorder(
    ICommandAuditService commandAuditService,
    IOptionsMonitor<ArchiveOptions> archiveOptions,
    ILogger<CommandAuditRecorder> logger)
{
    private const int SchemaVersion = 1;
    private const string UnknownDeviceId = "unknown-device";

    public CommandExecutionContext CreateContext(
        SignalWriteRequest request,
        ModbusWriteMode? writeMode)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = archiveOptions.CurrentValue;
        var signalId = string.IsNullOrWhiteSpace(request.SignalId)
            ? "<missing-signal>"
            : request.SignalId.Trim();
        return new CommandExecutionContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            sessionId: null,
            userId: null,
            username: null,
            string.IsNullOrWhiteSpace(options.DeviceId) ? UnknownDeviceId : options.DeviceId.Trim(),
            signalId,
            request.ValueType,
            CommandAuditValueFormatter.Format(request.Value, request.ValueType),
            writeMode,
            IsEmergency(signalId, options),
            SchemaVersion);
    }

    public async Task<ArchiveOperationResult> RecordRequestedAsync(
        CommandExecutionContext context,
        CancellationToken cancellationToken)
        => await RecordCommandAsync(
            CreateRecord(
                context,
                EquipmentCommandAuditResult.Requested,
                completedAtUtc: null,
                errorCode: null,
                errorMessage: null,
                CommandConfirmationStatus.Pending,
                confirmedAtUtc: null),
            cancellationToken,
            observeFailure: true).ConfigureAwait(false);

    public async Task RecordTerminalAsync(
        CommandExecutionContext context,
        EquipmentCommandAuditResult result,
        string? errorCode,
        string? errorMessage,
        CommandConfirmationStatus confirmationStatus,
        DateTimeOffset? confirmedAtUtc,
        CancellationToken cancellationToken = default)
        => await RecordCommandAsync(
            CreateRecord(
                context,
                result,
                DateTimeOffset.UtcNow,
                errorCode,
                errorMessage,
                confirmationStatus,
                confirmedAtUtc),
            cancellationToken,
            observeFailure: false).ConfigureAwait(false);

    public bool ShouldBlockCommandDelivery(
        CommandExecutionContext context,
        ArchiveOperationResult requestedAuditResult)
    {
        if (requestedAuditResult.Succeeded || context.IsEmergency)
        {
            return false;
        }

        return archiveOptions.CurrentValue.CommandAuditFailureMode == CommandAuditFailureMode.FailClosed;
    }

    private async Task<ArchiveOperationResult> RecordCommandAsync(
        EquipmentCommandAuditRecord record,
        CancellationToken cancellationToken,
        bool observeFailure)
    {
        var options = archiveOptions.CurrentValue;
        using var timeout = CreateAuditTimeout(options, cancellationToken);

        try
        {
            var result = await commandAuditService
                .RecordCommandAsync(record, timeout.Token)
                .ConfigureAwait(false);
            if (!result.Succeeded && observeFailure)
            {
                logger.LogWarning(
                    "Command audit enqueue failed for {SignalId}: {ErrorCode} {ErrorMessage}",
                    record.SignalId,
                    result.ErrorCode,
                    result.ErrorMessage);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            return ArchiveOperationResult.Failure(
                "CommandAuditEnqueueCanceled",
                "Command audit enqueue was canceled.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Command audit enqueue threw for {SignalId}", record.SignalId);
            return ArchiveOperationResult.Failure(
                "CommandAuditEnqueueFailed",
                "Command audit enqueue failed.",
                ex.Message);
        }
    }

    private static EquipmentCommandAuditRecord CreateRecord(
        CommandExecutionContext context,
        EquipmentCommandAuditResult result,
        DateTimeOffset? completedAtUtc,
        string? errorCode,
        string? errorMessage,
        CommandConfirmationStatus confirmationStatus,
        DateTimeOffset? confirmedAtUtc)
        => new(
            context.CommandId,
            context.CorrelationId,
            context.RequestedAtUtc,
            completedAtUtc,
            context.SessionId,
            context.UserId,
            context.Username,
            context.DeviceId,
            context.SignalId,
            context.ValueType,
            context.RequestedValueCanonical,
            context.WriteMode,
            result,
            errorCode,
            errorMessage,
            confirmationStatus,
            confirmedAtUtc,
            context.SchemaVersion);

    private static CancellationTokenSource CreateAuditTimeout(
        ArchiveOptions options,
        CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1, options.CommandAuditEnqueueTimeoutMs)));
        return timeout;
    }

    private static bool IsEmergency(string signalId, ArchiveOptions options)
        => options.EmergencySignalIds.Any(candidate =>
            string.Equals(candidate?.Trim(), signalId, StringComparison.OrdinalIgnoreCase));
}
