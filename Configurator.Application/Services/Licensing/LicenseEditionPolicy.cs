namespace Configurator.Application.Services.Licensing;

public static class LicenseEditionPolicy
{
    private static readonly IReadOnlySet<string> CommunityFeatures = new HashSet<string>(
        [LicenseFeature.RouteMap],
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> ProfessionalFeatures = new HashSet<string>(
        LicenseFeature.Known,
        StringComparer.Ordinal);

    public static bool Allows(LicenseEdition edition, IReadOnlyCollection<string> features)
    {
        ArgumentNullException.ThrowIfNull(features);

        var allowed = edition switch
        {
            LicenseEdition.Community => CommunityFeatures,
            LicenseEdition.Professional => ProfessionalFeatures,
            _ => null
        };

        return allowed is not null && features.All(allowed.Contains);
    }
}
