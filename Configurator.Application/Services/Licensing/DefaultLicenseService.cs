using System.Text.Json;
using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Authorization;

namespace Configurator.Application.Services.Licensing;

public sealed class DefaultLicenseService : ILicenseService, ILicenseStateAccessor
{
    private readonly ILicenseStore _licenseStore;
    private readonly ILicenseWritableStore? _licenseWritableStore;
    private readonly ILicenseVerifier _licenseVerifier;
    private readonly TimeProvider _timeProvider;
    private readonly IReadOnlyList<ISecurityAuditService> _securityAuditServices;
    private readonly IReadOnlyList<IUserSessionAccessor> _sessionAccessors;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private LicenseState _current;

    public DefaultLicenseService(
        ILicenseStore licenseStore,
        ILicenseVerifier licenseVerifier,
        TimeProvider timeProvider)
        : this(
            licenseStore,
            licenseWritableStore: null,
            licenseVerifier,
            timeProvider,
            [],
            [])
    {
    }

    public DefaultLicenseService(
        ILicenseStore licenseStore,
        ILicenseWritableStore licenseWritableStore,
        ILicenseVerifier licenseVerifier,
        TimeProvider timeProvider,
        IEnumerable<ISecurityAuditService> securityAuditServices,
        IEnumerable<IUserSessionAccessor> sessionAccessors)
        : this(
            licenseStore,
            licenseWritableStore,
            licenseVerifier,
            timeProvider,
            securityAuditServices?.ToArray() ?? [],
            sessionAccessors?.ToArray() ?? [])
    {
    }

    private DefaultLicenseService(
        ILicenseStore licenseStore,
        ILicenseWritableStore? licenseWritableStore,
        ILicenseVerifier licenseVerifier,
        TimeProvider timeProvider,
        IReadOnlyList<ISecurityAuditService> securityAuditServices,
        IReadOnlyList<IUserSessionAccessor> sessionAccessors)
    {
        _licenseStore = licenseStore ?? throw new ArgumentNullException(nameof(licenseStore));
        _licenseWritableStore = licenseWritableStore;
        _licenseVerifier = licenseVerifier ?? throw new ArgumentNullException(nameof(licenseVerifier));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _securityAuditServices = securityAuditServices;
        _sessionAccessors = sessionAccessors;
        _current = LicenseState.Missing(_timeProvider.GetUtcNow().ToUniversalTime());
    }

    public event EventHandler<LicenseStateChangedEventArgs>? StateChanged;

    public LicenseState Current => _current;

    public Task<LicenseState> GetCurrentAsync(CancellationToken cancellationToken = default)
        => RefreshAsync(cancellationToken);

    public async Task<LicenseState> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadCurrentCoreAsync(cancellationToken).ConfigureAwait(false);
            SetCurrent(state);

            return state;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task<LicenseInstallResult> InstallAsync(
        LicenseInstallRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.LicenseBytes);
        cancellationToken.ThrowIfCancellationRequested();
        var licenseBytes = request.LicenseBytes.ToArray();

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previous = Current;
            var previousLicenseId = LicenseId(previous);
            var sourceFileName = SafeFileName(request.SourceFileName);

            await AuditAsync(
                LicenseAuditEventNames.InstallAttempt,
                SecurityAuditResult.Succeeded,
                previousLicenseId,
                newLicenseId: null,
                reasonCode: null,
                sourceFileName,
                cancellationToken).ConfigureAwait(false);

            var validation = await _licenseVerifier
                .VerifyAsync(licenseBytes, cancellationToken)
                .ConfigureAwait(false);
            var evaluatedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime();
            var candidateState = LicenseState.FromValidation(validation, evaluatedAtUtc);
            var newLicenseId = LicenseId(candidateState);

            if (!validation.Succeeded)
            {
                var reason = candidateState.PrimaryErrorCode;
                await AuditAsync(
                    LicenseAuditEventNames.InstallFailed,
                    SecurityAuditResult.Failed,
                    previousLicenseId,
                    newLicenseId,
                    reason?.ToString(),
                    sourceFileName,
                    cancellationToken).ConfigureAwait(false);

                return LicenseInstallResult.Failure(
                    candidateState,
                    reason,
                    previousLicenseId,
                    newLicenseId,
                    candidateState.PrimaryErrorMessage ?? "License validation failed.");
            }

            if (_licenseWritableStore is null)
            {
                await AuditAsync(
                    LicenseAuditEventNames.InstallFailed,
                    SecurityAuditResult.Failed,
                    previousLicenseId,
                    newLicenseId,
                    LicenseValidationErrorCode.StoreUnavailable.ToString(),
                    sourceFileName,
                    cancellationToken).ConfigureAwait(false);

                return LicenseInstallResult.Failure(
                    previous,
                    LicenseValidationErrorCode.StoreUnavailable,
                    previousLicenseId,
                    newLicenseId,
                    "License store is not writable.");
            }

            var write = await _licenseWritableStore
                .ReplaceCurrentAsync(licenseBytes, cancellationToken)
                .ConfigureAwait(false);
            if (!write.Succeeded)
            {
                await AuditAsync(
                    LicenseAuditEventNames.InstallFailed,
                    SecurityAuditResult.Failed,
                    previousLicenseId,
                    newLicenseId,
                    write.ErrorCode ?? LicenseValidationErrorCode.StoreUnavailable.ToString(),
                    sourceFileName,
                    cancellationToken).ConfigureAwait(false);

                return LicenseInstallResult.Failure(
                    previous,
                    LicenseValidationErrorCode.StoreUnavailable,
                    previousLicenseId,
                    newLicenseId,
                    write.ErrorMessage ?? "License store is unavailable.");
            }

            SetCurrent(candidateState);
            await AuditAsync(
                LicenseAuditEventNames.InstallSucceeded,
                SecurityAuditResult.Succeeded,
                previousLicenseId,
                newLicenseId,
                reasonCode: null,
                sourceFileName,
                cancellationToken).ConfigureAwait(false);

            return LicenseInstallResult.Success(candidateState, previousLicenseId, newLicenseId);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public Task<LicenseValidationResult> VerifyAsync(
        byte[] licenseBytes,
        CancellationToken cancellationToken = default)
        => _licenseVerifier.VerifyAsync(licenseBytes, cancellationToken);

    private async Task<LicenseState> ReadCurrentCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var evaluatedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        var read = await _licenseStore.ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!read.Succeeded)
        {
            return LicenseState.StoreUnavailable(evaluatedAtUtc, read.ErrorMessage ?? "License store is unavailable.");
        }

        if (read.IsMissing)
        {
            return LicenseState.Missing(evaluatedAtUtc);
        }

        var validation = await _licenseVerifier.VerifyAsync(read.Bytes, cancellationToken).ConfigureAwait(false);

        return LicenseState.FromValidation(validation, evaluatedAtUtc);
    }

    private void SetCurrent(LicenseState state)
    {
        var previous = _current;
        _current = state;
        if (IsSameObservableState(previous, state))
        {
            return;
        }

        StateChanged?.Invoke(this, new LicenseStateChangedEventArgs(previous, state));
    }

    private async Task AuditAsync(
        string eventType,
        SecurityAuditResult result,
        string? previousLicenseId,
        string? newLicenseId,
        string? reasonCode,
        string? sourceFileName,
        CancellationToken cancellationToken)
    {
        if (_securityAuditServices.Count == 0)
        {
            return;
        }

        var session = _sessionAccessors.FirstOrDefault()?.Current.Session;
        var details = JsonSerializer.Serialize(
            new Dictionary<string, string?>
            {
                ["previousLicenseId"] = previousLicenseId,
                ["newLicenseId"] = newLicenseId,
                ["sourceFileName"] = sourceFileName,
            },
            LicenseJson.Options);
        var record = new SecurityAuditRecord(
            Guid.NewGuid(),
            _timeProvider.GetUtcNow().ToUniversalTime(),
            eventType,
            result == SecurityAuditResult.Succeeded ? SecurityAuditSeverity.Information : SecurityAuditSeverity.Warning,
            session?.UserId.ToString("D"),
            session?.Username,
            session?.SessionId.ToString("D"),
            targetUserId: null,
            result,
            reasonCode,
            details,
            schemaVersion: 1);

        foreach (var service in _securityAuditServices)
        {
            try
            {
                await service.RecordAsync(record, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
            }
        }
    }

    private static bool IsSameObservableState(LicenseState previous, LicenseState current)
        => previous.Status == current.Status
            && string.Equals(LicenseId(previous), LicenseId(current), StringComparison.Ordinal)
            && previous.PrimaryErrorCode == current.PrimaryErrorCode;

    private static string? LicenseId(LicenseState state)
        => state.Payload?.LicenseId.ToString("D");

    private static string? SafeFileName(string? sourceFileName)
        => string.IsNullOrWhiteSpace(sourceFileName)
            ? null
            : Path.GetFileName(sourceFileName);
}
