namespace Configurator.Application.Services.Licensing;

public sealed class LicenseEnvelope
{
    public string Format { get; set; } = string.Empty;

    public int SchemaVersion { get; set; }

    public string Algorithm { get; set; } = string.Empty;

    public string KeyId { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public string Signature { get; set; } = string.Empty;
}
