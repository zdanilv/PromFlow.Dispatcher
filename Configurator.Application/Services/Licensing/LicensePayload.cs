namespace Configurator.Application.Services.Licensing;

public sealed class LicensePayload
{
    public Guid LicenseId { get; set; }

    public string Product { get; set; } = string.Empty;

    public DateTimeOffset IssuedAtUtc { get; set; }

    public DateTimeOffset ValidFromUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public LicenseEdition Edition { get; set; }

    public int LicenseVersion { get; set; }

    public LicenseProductVersionRange ProductVersion { get; set; } = new();

    public List<string> Features { get; set; } = [];

    public LicenseCustomerProfile Customer { get; set; } = new();

    public LicenseOrganizationProfile Organization { get; set; } = new();

    public LicenseInstallationProfile Installation { get; set; } = new();
}

public sealed class LicenseProductVersionRange
{
    public string Minimum { get; set; } = "1.0.0";

    public string MaximumExclusive { get; set; } = "2.0.0";
}

public sealed class LicenseCustomerProfile
{
    public string FullName { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
}

public sealed class LicenseOrganizationProfile
{
    public string Name { get; set; } = string.Empty;

    public string SiteAddress { get; set; } = string.Empty;
}

public sealed class LicenseInstallationProfile
{
    public LicenseInstallationBindingMode BindingMode { get; set; } = LicenseInstallationBindingMode.InstallationId;

    public string InstallationId { get; set; } = string.Empty;
}
