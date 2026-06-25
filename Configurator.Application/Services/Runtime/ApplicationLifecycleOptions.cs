namespace Configurator.Application.Services.Runtime;

public sealed class ApplicationLifecycleOptions
{
    public const string SectionName = "Lifecycle";

    public int StartupTimeoutSeconds { get; set; } = 30;

    public int ShutdownTimeoutSeconds { get; set; } = 10;

    public int ModbusStopTimeoutSeconds { get; set; } = 5;

    public int ArchiveFlushTimeoutSeconds { get; set; } = 5;

    public int SingleInstanceLockTimeoutMilliseconds { get; set; }

    public string SingleInstanceMutexName { get; set; } = "PromFlow.Dispatcher";

    public string LockFileName { get; set; } = "promflow-dispatcher.lock";

    public ApplicationLifecycleOptions Clone()
        => new()
        {
            StartupTimeoutSeconds = StartupTimeoutSeconds,
            ShutdownTimeoutSeconds = ShutdownTimeoutSeconds,
            ModbusStopTimeoutSeconds = ModbusStopTimeoutSeconds,
            ArchiveFlushTimeoutSeconds = ArchiveFlushTimeoutSeconds,
            SingleInstanceLockTimeoutMilliseconds = SingleInstanceLockTimeoutMilliseconds,
            SingleInstanceMutexName = SingleInstanceMutexName,
            LockFileName = LockFileName
        };
}
