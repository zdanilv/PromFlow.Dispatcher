using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Licensing;
using Xunit;

namespace Configurator.Tests.Unit.Authorization;

public sealed class AccessDecisionServiceTests
{
    [Fact]
    public void Authorize_DeniesAnonymousSession()
    {
        var accessor = new TestSessionAccessor();
        var service = new DefaultAccessDecisionService(accessor, new NoLicenseFeatureGate());

        var decision = service.Authorize(new AccessRequirement(Permission.ViewRouteMap));

        Assert.False(decision.Succeeded);
        Assert.Equal("NotAuthenticated", decision.ReasonCode);
    }

    [Fact]
    public void Authorize_AllowsUserPermissionAndDeniesMissingPermission()
    {
        var accessor = new TestSessionAccessor();
        accessor.SetCurrent(CreateSession(
            UserRole.User,
            Permission.ViewRouteMap,
            Permission.IssueEquipmentCommands));
        var service = new DefaultAccessDecisionService(accessor, new NoLicenseFeatureGate());

        var allowed = service.Authorize(new AccessRequirement(Permission.ViewRouteMap));
        var denied = service.Authorize(new AccessRequirement(Permission.ViewSignalMapping));

        Assert.True(allowed.Succeeded);
        Assert.False(denied.Succeeded);
        Assert.Equal("PermissionDenied", denied.ReasonCode);
    }

    [Fact]
    public void Authorize_AllowsAdministratorPermissionButDeniesMissingLicenseFeature()
    {
        var accessor = new TestSessionAccessor();
        accessor.SetCurrent(CreateSession(UserRole.Administrator, Enum.GetValues<Permission>()));
        var service = new DefaultAccessDecisionService(
            accessor,
            new LicenseFeatureGate(new TestLicenseStateAccessor(ValidState(LicenseFeature.RouteMap))));

        var permissionAllowed = service.Authorize(new AccessRequirement(Permission.ManageUsers));
        var featureAllowed = service.Authorize(new AccessRequirement(Permission.ViewRouteMap, LicenseFeature.RouteMap));
        var featureDenied = service.Authorize(new AccessRequirement(Permission.ViewModbusDiagnostics, LicenseFeature.Diagnostics));

        Assert.True(permissionAllowed.Succeeded);
        Assert.True(featureAllowed.Succeeded);
        Assert.False(featureDenied.Succeeded);
        Assert.Equal("LicenseFeatureUnavailable", featureDenied.ReasonCode);
    }

    private static UserSession CreateSession(UserRole role, params Permission[] permissions) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            role == UserRole.Administrator ? "admin" : "operator",
            role,
            permissions,
            DateTimeOffset.UtcNow);

    private sealed class TestSessionAccessor : IUserSessionAccessor
    {
        public UserSessionSnapshot Current { get; private set; } = UserSessionSnapshot.Anonymous;

        public void SetCurrent(UserSession session) =>
            Current = UserSessionSnapshot.Authenticated(session);

        public void Clear() => Current = UserSessionSnapshot.Anonymous;

        public bool ClearIfCurrent(Guid userId)
        {
            if (userId == Guid.Empty)
            {
                throw new ArgumentException("User id must not be empty.", nameof(userId));
            }

            if (Current.Session?.UserId != userId)
            {
                return false;
            }

            Current = UserSessionSnapshot.Anonymous;
            return true;
        }
    }

    private sealed class TestLicenseStateAccessor(LicenseState current) : ILicenseStateAccessor
    {
        public LicenseState Current { get; } = current;
        public event EventHandler<LicenseStateChangedEventArgs>? StateChanged { add { } remove { } }
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
}
