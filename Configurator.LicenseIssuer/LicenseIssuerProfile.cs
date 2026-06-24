using Configurator.Application.Services.Licensing;

namespace Configurator.LicenseIssuer;

public sealed class LicenseIssuerProfile
{
    public Guid LicenseId { get; set; }

    public string Product { get; set; } = LicenseConstants.Product;

    public DateTimeOffset? IssuedAtUtc { get; set; }

    public DateTimeOffset ValidFromUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public LicenseEdition Edition { get; set; } = LicenseEdition.Professional;

    public int LicenseVersion { get; set; } = LicenseConstants.LicenseVersion;

    public LicenseProductVersionRange ProductVersion { get; set; } = new();

    public List<string> Features { get; set; } = [];

    public LicenseCustomerProfile Customer { get; set; } = new();

    public LicenseOrganizationProfile Organization { get; set; } = new();

    public LicenseInstallationProfile Installation { get; set; } = new();

    public LicensePayload ToPayload(DateTimeOffset nowUtc)
        => new()
        {
            LicenseId = LicenseId == Guid.Empty ? Guid.NewGuid() : LicenseId,
            Product = Product,
            IssuedAtUtc = IssuedAtUtc?.ToUniversalTime() ?? nowUtc.ToUniversalTime(),
            ValidFromUtc = ValidFromUtc.ToUniversalTime(),
            ExpiresAtUtc = ExpiresAtUtc.ToUniversalTime(),
            Edition = Edition,
            LicenseVersion = LicenseVersion,
            ProductVersion = ProductVersion,
            Features = Features.ToList(),
            Customer = Customer,
            Organization = Organization,
            Installation = Installation
        };
}
