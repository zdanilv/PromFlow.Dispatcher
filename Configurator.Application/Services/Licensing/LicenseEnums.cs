namespace Configurator.Application.Services.Licensing;

public enum LicenseEdition
{
    Community = 0,
    Professional = 1
}

public enum LicenseInstallationBindingMode
{
    None = 0,
    InstallationId = 1
}

public enum LicenseStatus
{
    Missing = 0,
    Valid = 1,
    Invalid = 2,
    NotYetValid = 3,
    Expired = 4,
    ProductMismatch = 5,
    ProductVersionMismatch = 6,
    InstallationMismatch = 7,
    ClockRollbackDetected = 8,
    StoreUnavailable = 9
}

public enum LicenseValidationErrorCode
{
    FileTooLarge = 0,
    InvalidUtf8 = 1,
    InvalidJson = 2,
    InvalidEnvelopeFormat = 3,
    UnsupportedSchemaVersion = 4,
    UnsupportedAlgorithm = 5,
    UnknownKeyId = 6,
    InvalidBase64Url = 7,
    InvalidSignature = 8,
    InvalidPayloadJson = 9,
    ProductMismatch = 10,
    SemanticFieldInvalid = 11,
    NotYetValid = 12,
    Expired = 13,
    ProductVersionUnsupported = 14,
    InstallationMismatch = 15,
    ClockRollbackDetected = 16,
    FeatureEditionInconsistent = 17,
    TestKeyRejected = 18,
    StoreUnavailable = 19
}
