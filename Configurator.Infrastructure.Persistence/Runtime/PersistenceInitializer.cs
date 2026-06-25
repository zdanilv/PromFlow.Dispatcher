using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Runtime;
using Configurator.Infrastructure.Persistence.Archive;
using Configurator.Infrastructure.Persistence.Common;
using Configurator.Infrastructure.Persistence.Security;
using Configurator.Infrastructure.Persistence.Sqlite;
using Microsoft.Extensions.Options;

namespace Configurator.Infrastructure.Persistence.Runtime;

public sealed class PersistenceInitializer : IPersistenceInitializer
{
    private readonly SecuritySqliteMigrationRunner _securityMigrationRunner;
    private readonly SqliteMigrationRunner _archiveMigrationRunner;
    private readonly ArchiveOptionsValidator _archiveOptionsValidator;
    private readonly ArchivePartitionResolver _partitionResolver;
    private readonly IOptions<ArchiveOptions> _archiveOptions;

    public PersistenceInitializer(
        SecuritySqliteMigrationRunner securityMigrationRunner,
        SqliteMigrationRunner archiveMigrationRunner,
        ArchiveOptionsValidator archiveOptionsValidator,
        ArchivePartitionResolver partitionResolver,
        IOptions<ArchiveOptions> archiveOptions)
    {
        _securityMigrationRunner = securityMigrationRunner ?? throw new ArgumentNullException(nameof(securityMigrationRunner));
        _archiveMigrationRunner = archiveMigrationRunner ?? throw new ArgumentNullException(nameof(archiveMigrationRunner));
        _archiveOptionsValidator = archiveOptionsValidator ?? throw new ArgumentNullException(nameof(archiveOptionsValidator));
        _partitionResolver = partitionResolver ?? throw new ArgumentNullException(nameof(partitionResolver));
        _archiveOptions = archiveOptions ?? throw new ArgumentNullException(nameof(archiveOptions));
    }

    public async Task<PersistenceInitializationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        var steps = new List<ApplicationRuntimeStepResult>();

        var securityResult = await _securityMigrationRunner.InitializeAsync(cancellationToken).ConfigureAwait(false);
        AddArchiveResult(
            steps,
            "SecurityPersistenceMigrations",
            securityResult,
            isFatal: true);
        if (!securityResult.Succeeded)
        {
            return new PersistenceInitializationResult(steps);
        }

        var options = _archiveOptions.Value.Clone();
        if (!options.Enabled)
        {
            steps.Add(ApplicationRuntimeStepResult.Success("ArchivePersistenceSkipped"));
            return new PersistenceInitializationResult(steps);
        }

        var validation = _archiveOptionsValidator.Validate(options);
        if (!validation.Succeeded)
        {
            var details = string.Join("; ", validation.Errors.Select(error => $"{error.Code}:{error.PropertyName}"));
            steps.Add(ApplicationRuntimeStepResult.Failure(
                "ArchivePersistenceMigrations",
                "ArchiveOptionsInvalid",
                details,
                isFatal: false));
            return new PersistenceInitializationResult(steps);
        }

        try
        {
            var partition = _partitionResolver.GetWritablePartition(options, DateTimeOffset.UtcNow);
            var initializationOptions = new ArchiveDatabaseInitializationOptions
            {
                DatabasePath = partition.DatabasePath,
                DeviceId = options.DeviceId.Trim(),
                ApplicationVersion = typeof(PersistenceInitializer).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                ArchiveSchemaVersion = 1,
                BusyTimeoutMs = options.BusyTimeoutMs
            };

            var archiveResult = await _archiveMigrationRunner
                .InitializeAsync(initializationOptions, cancellationToken)
                .ConfigureAwait(false);
            AddArchiveResult(
                steps,
                "ArchivePersistenceMigrations",
                archiveResult,
                isFatal: false);
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            steps.Add(ApplicationRuntimeStepResult.Failure(
                "ArchivePersistenceMigrations",
                "ArchivePersistenceInitializationFailed",
                ex.Message,
                isFatal: false));
        }

        return new PersistenceInitializationResult(steps);
    }

    private static void AddArchiveResult(
        List<ApplicationRuntimeStepResult> steps,
        string step,
        ArchiveOperationResult result,
        bool isFatal)
    {
        steps.Add(result.Succeeded
            ? ApplicationRuntimeStepResult.Success(step)
            : ApplicationRuntimeStepResult.Failure(
                step,
                result.ErrorCode ?? step + "Failed",
                result.ErrorMessage ?? step + " failed.",
                isFatal));
    }
}
