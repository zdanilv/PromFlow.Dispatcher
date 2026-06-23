using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Signals;
using Configurator.Infrastructure.Modbus.Archiving;
using Configurator.Infrastructure.Modbus.RouteMap;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests.Archiving;

public sealed class ModbusCommandAuditTests
{
    [Fact]
    public async Task Dispatcher_MissingSignal_AuditsRejectedCommandWithNullableWriteMode()
    {
        var audit = new RecordingCommandAuditService();
        var dispatcher = CreateDispatcher(
            new RecordingModbusTcpService(),
            new ModbusOptions(),
            new ArchiveOptions(),
            audit);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchAsync(new SignalWriteRequest("missing.signal", true, SignalValueType.Bool)));

        Assert.Contains(audit.Commands, record =>
            record.Result == EquipmentCommandAuditResult.Requested
            && record.SignalId == "missing.signal"
            && record.WriteMode is null);
        var terminal = Assert.Single(audit.Commands, record =>
            record.Result == EquipmentCommandAuditResult.Failed);
        Assert.Equal("ModbusDataPointMissing", terminal.ErrorCode);
        Assert.Equal(CommandConfirmationStatus.Rejected, terminal.ConfirmationStatus);
        Assert.Null(terminal.WriteMode);
    }

    [Fact]
    public async Task Dispatcher_FailClosedBlocksOrdinaryCommandBeforeModbusWrite()
    {
        var service = new RecordingModbusTcpService();
        var audit = new RecordingCommandAuditService
        {
            RequestedResult = ArchiveOperationResult.Failure(
                "ArchiveQueueStopped",
                "Archive queue is stopped.")
        };
        var archiveOptions = new ArchiveOptions
        {
            CommandAuditFailureMode = CommandAuditFailureMode.FailClosed
        };
        var dispatcher = CreateDispatcher(
            service,
            CreateModbusOptions(CreatePoint("ordinary.command")),
            archiveOptions,
            audit);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchAsync(new SignalWriteRequest("ordinary.command", true, SignalValueType.Bool)));

        Assert.Empty(service.Writes);
        Assert.Contains(audit.Commands, record =>
            record.Result == EquipmentCommandAuditResult.Failed
            && record.ConfirmationStatus == CommandConfirmationStatus.Rejected);
    }

    [Fact]
    public async Task Dispatcher_EmergencyCommandFailOpensWhenAuditRequestFails()
    {
        var service = new RecordingModbusTcpService();
        var audit = new RecordingCommandAuditService
        {
            RequestedResult = ArchiveOperationResult.Failure(
                "ArchiveQueueStopped",
                "Archive queue is stopped.")
        };
        var archiveOptions = new ArchiveOptions
        {
            CommandAuditFailureMode = CommandAuditFailureMode.FailClosed,
            EmergencySignalIds = ["system.emergency"]
        };
        var dispatcher = CreateDispatcher(
            service,
            CreateModbusOptions(CreatePoint("system.emergency")),
            archiveOptions,
            audit);

        await dispatcher.DispatchAsync(new SignalWriteRequest("system.emergency", true, SignalValueType.Bool));

        var write = Assert.Single(service.Writes);
        Assert.Equal("system.emergency", write.SignalId);
        Assert.Equal(true, write.Value);
        Assert.NotNull(write.CommandId);
        Assert.Contains(audit.Commands, record =>
            record.CommandId == write.CommandId
            && record.Result == EquipmentCommandAuditResult.Succeeded);
    }

    [Fact]
    public async Task Dispatcher_PulseResetCancellationWithoutCallerCancellation_AuditsFailed()
    {
        var service = new RecordingModbusTcpService { CancelFalseWrites = true };
        var audit = new RecordingCommandAuditService();
        var point = CreatePoint("pulse.command");
        point.WriteMode = ModbusWriteMode.Pulse;
        point.PulseDurationMs = 1;
        var dispatcher = CreateDispatcher(
            service,
            CreateModbusOptions(point),
            new ArchiveOptions(),
            audit);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchAsync(new SignalWriteRequest("pulse.command", true, SignalValueType.Bool)));

        var terminal = Assert.Single(audit.Commands, record =>
            record.Result == EquipmentCommandAuditResult.Failed);
        Assert.Equal("ModbusPulseResetCanceled", terminal.ErrorCode);
        Assert.Equal(CommandConfirmationStatus.ConnectionLost, terminal.ConfirmationStatus);
    }


    private static ModbusTcpCommandDispatcher CreateDispatcher(
        IModbusTcpService service,
        ModbusOptions modbusOptions,
        ArchiveOptions archiveOptions,
        RecordingCommandAuditService audit)
        => new(
            service,
            new TestOptionsMonitor<ModbusOptions>(modbusOptions),
            new CommandAuditRecorder(
                audit,
                new TestOptionsMonitor<ArchiveOptions>(archiveOptions),
                NullLogger<CommandAuditRecorder>.Instance));

    private static ModbusOptions CreateModbusOptions(ModbusDataPointOptions point)
        => new() { DataMap = [point] };

    private static ModbusDataPointOptions CreatePoint(string name)
        => new()
        {
            Name = name,
            Area = ModbusDataArea.Coil,
            Address = 0,
            Length = 1,
            Access = ModbusDataAccess.ReadWrite,
            Type = ModbusValueType.Bool,
            WriteMode = ModbusWriteMode.Latched
        };

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;

        public T Get(string? name)
            => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener)
            => null;
    }

    private sealed class RecordingCommandAuditService : ICommandAuditService
    {
        public List<EquipmentCommandAuditRecord> Commands { get; } = [];

        public ArchiveOperationResult RequestedResult { get; set; } = ArchiveOperationResult.Success();

        public Task<ArchiveOperationResult> RecordCommandAsync(
            EquipmentCommandAuditRecord record,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(record);
            return Task.FromResult(
                record.Result == EquipmentCommandAuditResult.Requested
                    ? RequestedResult
                    : ArchiveOperationResult.Success());
        }

        public Task<ArchiveOperationResult> RecordPhysicalWriteAsync(
            PhysicalModbusWriteAuditRecord record,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ArchiveOperationResult.Success());
    }

    private sealed class RecordingModbusTcpService : IModbusTcpService
    {
        public List<(string SignalId, object? Value, Guid? CommandId)> Writes { get; } = [];

        public bool CancelFalseWrites { get; init; }

        public ModbusServiceState State { get; } = ModbusServiceState.Stopped;

        public event EventHandler<ModbusServiceState>? StateChanged
        {
            add { }
            remove { }
        }

        public Task<ModbusOperationResult> StartClientAsync(ModbusOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(ModbusOperationResult.Success());

        public Task<ModbusOperationResult> StartServerAsync(ModbusOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(ModbusOperationResult.Success());

        public Task<ModbusOperationResult> StopAsync(CancellationToken ct = default)
            => Task.FromResult(ModbusOperationResult.Success());

        public Task<ModbusOperationResult<T>> GetAsync<T>(string name, CancellationToken ct = default)
            => Task.FromResult(ModbusOperationResult<T>.Failure("Unavailable", "Unavailable"));

        public Task<ModbusOperationResult> SetAsync<T>(string name, T value, CancellationToken ct = default)
            => SetAsync(name, value, context: null, ct);

        public Task<ModbusOperationResult> SetAsync<T>(
            string name,
            T value,
            CommandExecutionContext? context,
            CancellationToken ct = default)
        {
            if (CancelFalseWrites && value is bool boolValue && !boolValue)
            {
                throw new OperationCanceledException(ct);
            }

            Writes.Add((name, value, context?.CommandId));
            return Task.FromResult(ModbusOperationResult.Success());
        }

        public IDisposable Subscribe(string name, Action<ModbusDataValue> onChanged)
            => new NoopDisposable();

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
