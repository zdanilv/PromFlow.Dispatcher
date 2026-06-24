using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Licensing;
using Xunit;

namespace Configurator.Tests.Unit.Licensing;

public sealed class LicenseFeatureGateTests
{
    private static readonly UserSession AdminSession = new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "admin",
        UserRole.Administrator,
        Enum.GetValues<Permission>(),
        DateTimeOffset.UtcNow);

    [Fact]
    public void AuthorizeFeature_AllowsNullFeatureWithoutLicense()
    {
        var gate = new LicenseFeatureGate(new StateAccessor(LicenseState.Missing(DateTimeOffset.UtcNow)));

        var decision = gate.AuthorizeFeature(new AccessRequirement(Permission.ViewLicense), AdminSession);

        Assert.True(decision.Succeeded);
    }

    [Theory]
    [InlineData(LicenseStatus.Missing, "LicenseMissing")]
    [InlineData(LicenseStatus.Expired, "LicenseExpired")]
    [InlineData(LicenseStatus.ClockRollbackDetected, "LicenseClockRollback")]
    public void AuthorizeFeature_DeniesNonNullFeatureWhenCurrentLicenseIsNotValid(
        LicenseStatus status,
        string reasonCode)
    {
        var gate = new LicenseFeatureGate(new StateAccessor(InvalidState(status)));

        var decision = gate.AuthorizeFeature(
            new AccessRequirement(Permission.ViewRouteMap, LicenseFeature.RouteMap),
            AdminSession);

        Assert.False(decision.Succeeded);
        Assert.Equal(reasonCode, decision.ReasonCode);
    }

    [Fact]
    public void AuthorizeFeature_AllowsPresentFeatureAndDeniesMissingFeatureForAdministrator()
    {
        var gate = new LicenseFeatureGate(new StateAccessor(ValidState(LicenseFeature.RouteMap)));

        var allowed = gate.AuthorizeFeature(
            new AccessRequirement(Permission.ViewRouteMap, LicenseFeature.RouteMap),
            AdminSession);
        var denied = gate.AuthorizeFeature(
            new AccessRequirement(Permission.ViewModbusDiagnostics, LicenseFeature.Diagnostics),
            AdminSession);

        Assert.True(allowed.Succeeded);
        Assert.False(denied.Succeeded);
        Assert.Equal("LicenseFeatureUnavailable", denied.ReasonCode);
    }

    private static LicenseState InvalidState(LicenseStatus status)
    {
        if (status == LicenseStatus.Missing)
        {
            return LicenseState.Missing(DateTimeOffset.UtcNow);
        }

        var code = status switch
        {
            LicenseStatus.Expired => LicenseValidationErrorCode.Expired,
            LicenseStatus.ClockRollbackDetected => LicenseValidationErrorCode.ClockRollbackDetected,
            _ => LicenseValidationErrorCode.InvalidJson,
        };

        return new LicenseState(
            status,
            DateTimeOffset.UtcNow,
            null,
            [new LicenseValidationError(code, status.ToString())]);
    }

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

    private sealed class StateAccessor(LicenseState current) : ILicenseStateAccessor
    {
        public LicenseState Current { get; } = current;
        public event EventHandler<LicenseStateChangedEventArgs>? StateChanged { add { } remove { } }
    }
}
