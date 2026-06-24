namespace Configurator.Application.Services.Licensing;

public sealed record LicenseState(
    LicenseStatus Status,
    DateTimeOffset EvaluatedAtUtc,
    LicensePayload? Payload,
    IReadOnlyList<LicenseValidationError> Errors)
{
    public bool IsValid => Status == LicenseStatus.Valid;

    public LicenseValidationErrorCode? PrimaryErrorCode => Errors.FirstOrDefault()?.Code;

    public string? PrimaryErrorMessage => Errors.FirstOrDefault()?.Message;

    public static LicenseState FromValidation(LicenseValidationResult result, DateTimeOffset evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new LicenseState(result.Status, evaluatedAtUtc.ToUniversalTime(), result.Payload, result.Errors);
    }

    public static LicenseState Missing(DateTimeOffset evaluatedAtUtc)
        => new(LicenseStatus.Missing, evaluatedAtUtc.ToUniversalTime(), null, []);

    public static LicenseState StoreUnavailable(DateTimeOffset evaluatedAtUtc, string message)
        => new(
            LicenseStatus.StoreUnavailable,
            evaluatedAtUtc.ToUniversalTime(),
            null,
            [new LicenseValidationError(LicenseValidationErrorCode.StoreUnavailable, message)]);
}
