namespace Configurator.Application.Services.Licensing;

public sealed class LicensingOptions
{
    public const string SectionName = "Licensing";

    public string Product { get; set; } = LicenseConstants.Product;

    public string ProductVersion { get; set; } = "1.0.0";

    public string LicenseDirectory { get; set; } = string.Empty;

    public string LicenseFileName { get; set; } = "current.promlicense";

    public string InstallationIdentityFileName { get; set; } = "installation.json";

    public string TrustedTimeStateFileName { get; set; } = "trusted-time.json";

    public int AllowedClockSkewMinutes { get; set; } = 5;

    public LicenseInstallationBindingMode BindingMode { get; set; } = LicenseInstallationBindingMode.InstallationId;

    public int MaxLicenseFileBytes { get; set; } = 65_536;

    public bool AllowTestKeys { get; set; }

    public List<TrustedLicensePublicKey> TrustedPublicKeys { get; set; } = [];

    public LicensingOptions Clone()
        => new()
        {
            Product = Product,
            ProductVersion = ProductVersion,
            LicenseDirectory = LicenseDirectory,
            LicenseFileName = LicenseFileName,
            InstallationIdentityFileName = InstallationIdentityFileName,
            TrustedTimeStateFileName = TrustedTimeStateFileName,
            AllowedClockSkewMinutes = AllowedClockSkewMinutes,
            BindingMode = BindingMode,
            MaxLicenseFileBytes = MaxLicenseFileBytes,
            AllowTestKeys = AllowTestKeys,
            TrustedPublicKeys = TrustedPublicKeys.Select(key => key.Clone()).ToList()
        };
}

public sealed class TrustedLicensePublicKey
{
    public string KeyId { get; set; } = string.Empty;

    public string PublicKeyPem { get; set; } = string.Empty;

    public bool IsTestKey { get; set; }

    public TrustedLicensePublicKey Clone()
        => new()
        {
            KeyId = KeyId,
            PublicKeyPem = PublicKeyPem,
            IsTestKey = IsTestKey
        };
}
