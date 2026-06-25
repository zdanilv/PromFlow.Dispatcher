using Configurator.Application.Services.Runtime;

namespace Configurator.Infrastructure.Runtime;

public sealed class FileApplicationInstanceGuard : IApplicationInstanceGuard
{
    private readonly ApplicationRuntimePathProvider _pathProvider;
    private readonly ApplicationLifecycleOptions _options;

    public FileApplicationInstanceGuard(
        ApplicationRuntimePathProvider pathProvider,
        ApplicationLifecycleOptions options)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _options = options?.Clone() ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<IApplicationInstanceLease> AcquireAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var mutexName = NormalizeMutexName(_options.SingleInstanceMutexName);
        var mutex = new Mutex(false, mutexName);
        var timeout = TimeSpan.FromMilliseconds(Math.Max(0, _options.SingleInstanceLockTimeoutMilliseconds));
        var mutexAcquired = false;
        FileStream? lockFile = null;

        try
        {
            try
            {
                mutexAcquired = mutex.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                mutexAcquired = true;
            }
            if (!mutexAcquired)
            {
                throw new InvalidOperationException("Another PromFlow.Dispatcher instance is already running.");
            }

            var lockPath = _pathProvider.GetLockFilePath();
            var directory = Path.GetDirectoryName(lockPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("Application lock file directory is invalid.");
            }

            Directory.CreateDirectory(directory);
            lockFile = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough);
            lockFile.SetLength(0);
            using (var writer = new StreamWriter(lockFile, leaveOpen: true))
            {
                writer.WriteLine(Environment.ProcessId);
                writer.WriteLine(DateTimeOffset.UtcNow.ToString("O"));
                writer.Flush();
            }

            lockFile.Position = 0;
            return Task.FromResult<IApplicationInstanceLease>(new FileApplicationInstanceLease(
                mutex,
                lockFile,
                lockPath,
                mutexAcquired));
        }
        catch
        {
            lockFile?.Dispose();
            if (mutexAcquired)
            {
                mutex.ReleaseMutex();
            }

            mutex.Dispose();
            throw;
        }
    }

    private static string NormalizeMutexName(string? configuredName)
    {
        var name = string.IsNullOrWhiteSpace(configuredName)
            ? "PromFlow.Dispatcher"
            : configuredName.Trim();

        return OperatingSystem.IsWindows()
            ? @"Local\" + name.Replace('\\', '_')
            : name.Replace(Path.DirectorySeparatorChar, '_');
    }

    private sealed class FileApplicationInstanceLease : IApplicationInstanceLease
    {
        private readonly Mutex _mutex;
        private readonly FileStream _lockFile;
        private readonly bool _mutexAcquired;
        private bool _disposed;

        public FileApplicationInstanceLease(
            Mutex mutex,
            FileStream lockFile,
            string lockFilePath,
            bool mutexAcquired)
        {
            _mutex = mutex;
            _lockFile = lockFile;
            Description = lockFilePath;
            _mutexAcquired = mutexAcquired;
        }

        public string Description { get; }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            _lockFile.Dispose();
            if (_mutexAcquired)
            {
                _mutex.ReleaseMutex();
            }

            _mutex.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
