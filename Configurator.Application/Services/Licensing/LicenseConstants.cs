namespace Configurator.Application.Services.Licensing;

public static class LicenseConstants
{
    public const string EnvelopeFormat = "PromFlow.License";
    public const int SchemaVersion = 1;
    public const string Algorithm = "ES256";
    public const string Product = "PromFlow.Dispatcher";
    public const int LicenseVersion = 1;
    public const int EcdsaP256SignatureLength = 64;
    public const string RequestFormat = "PromFlow.LicenseRequest";
}
