using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Licensing;
using Xunit;

namespace Configurator.Tests.Unit.Licensing;

public sealed class LicenseSessionExpiryAcceptanceTests
{
    [Fact]
    public void ExpiredLicenseState_DeniesNextLicensedAccessEvenForAdministrator()
    {
        var accessor = new MutableLicenseStateAccessor(ValidState(LicenseFeature.RouteMap));
        var gate = new LicenseFeatureGate(accessor);
        var session = CreateAdministratorSession();
        var requirement = new AccessRequirement(Permission.ViewRouteMap, LicenseFeature.RouteMap);

        var allowed = gate.AuthorizeFeature(requirement, session);
        accessor.Current = ExpiredState();
        var denied = gate.AuthorizeFeature(requirement, session);

        Assert.True(allowed.Succeeded);
        Assert.False(denied.Succeeded);
        Assert.Equal("LicenseExpired", denied.ReasonCode);
        Assert.Equal(session.UserId, denied.UserId);
    }

    private static UserSession CreateAdministratorSession()
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "admin",
            UserRole.Administrator,
            Enum.GetValues<Permission>(),
            DateTimeOffset.UtcNow);

    private static LicenseState ValidState(params string[] features)
        => new(
            LicenseStatus.Valid,
            DateTimeOffset.UtcNow,
            new LicensePayload
            {
                LicenseId = Guid.NewGuid(),
                Product = LicenseConstants.Product,
                IssuedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
                ValidFromUtc = DateTimeOffset.UtcNow.AddDays(-1),
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
                Edition = LicenseEdition.Professional,
                LicenseVersion = LicenseConstants.LicenseVersion,
                ProductVersion = new LicenseProductVersionRange(),
                Features = [.. features],
                Installation = new LicenseInstallationProfile
                {
                    BindingMode = LicenseInstallationBindingMode.InstallationId,
                    InstallationId = "installation-1"
                }
            },
            []);

    private static LicenseState ExpiredState()
        => new(
            LicenseStatus.Expired,
            DateTimeOffset.UtcNow,
            null,
            [new LicenseValidationError(LicenseValidationErrorCode.Expired, "License has expired.")]);

    private sealed class MutableLicenseStateAccessor(LicenseState current) : ILicenseStateAccessor
    {
        public LicenseState Current { get; set; } = current;
        public event EventHandler<LicenseStateChangedEventArgs>? StateChanged { add { } remove { } }
    }
}
