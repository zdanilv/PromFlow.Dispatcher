namespace Configurator.Application.Services.Licensing;

public sealed record LicenseValidationError(
    LicenseValidationErrorCode Code,
    string Message,
    string? Field = null);

public sealed class LicenseValidationResult
{
    private LicenseValidationResult(
        LicenseStatus status,
        LicenseEnvelope? envelope,
        LicensePayload? payload,
        IReadOnlyList<LicenseValidationError> errors)
    {
        Status = status;
        Envelope = envelope;
        Payload = payload;
        Errors = errors;
    }

    public LicenseStatus Status { get; }

    public bool Succeeded => Status == LicenseStatus.Valid;

    public LicenseEnvelope? Envelope { get; }

    public LicensePayload? Payload { get; }

    public IReadOnlyList<LicenseValidationError> Errors { get; }

    public static LicenseValidationResult Success(LicenseEnvelope envelope, LicensePayload payload)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(payload);

        return new LicenseValidationResult(LicenseStatus.Valid, envelope, payload, []);
    }

    public static LicenseValidationResult Failure(
        LicenseStatus status,
        LicenseValidationErrorCode code,
        string message,
        string? field = null,
        LicenseEnvelope? envelope = null)
        => new(status, envelope, null, [new LicenseValidationError(code, message, field)]);
}
